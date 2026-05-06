#!/usr/bin/env bash
#
# Local development setup for the .NET Agent Framework.
# Provisions Azure AI Services via Terraform and generates appsettings.Local.json from templates.
#
# Usage:
#   ./infra/setup-local.sh            # provision + generate configs
#   ./infra/setup-local.sh --cleanup  # destroy resources + remove configs

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TERRAFORM_DIR="$REPO_ROOT/infra/terraform/local-dev"

# ── Port Map ────────────────────────────────────────────────────────────────
declare -A PORT_MAP=(
    [5001]="crm-api"
    [5002]="crm-mcp"
    [5003]="knowledge-mcp"
    [5004]="crm-agent"
    [5005]="product-agent"
    [5006]="orchestrator-agent"
    [5007]="bff-api"
    [5008]="blazor-ui"
)

# Templates that need Foundry placeholder replacement
TEMPLATE_COMPONENTS=(
    "simple-agent"
    "knowledge-mcp"
    "crm-agent"
    "product-agent"
    "orchestrator-agent"
    "bff-api"
    "blazor-ui"
)

# Templates that are static (no placeholder replacement needed)
STATIC_COMPONENTS=(
    "crm-api"
    "crm-mcp"
)

# ── Helper Functions ────────────────────────────────────────────────────────

step()  { echo -e "\n\033[36m==> $1\033[0m"; }
ok()    { echo -e "  \033[32m[OK]\033[0m $1"; }
warn()  { echo -e "  \033[33m[WARN]\033[0m $1"; }
fail()  { echo -e "  \033[31m[FAIL]\033[0m $1"; }

command_exists() { command -v "$1" &>/dev/null; }

# ── Working RG (used by both cleanup and main) ──────────────────────────────
WORKING_RG="rg-dotnetagent-localdev"
WORKING_LOCATION="${TF_VAR_location:-centralus}"

# ── Backend Bootstrap (shared by cleanup and main path) ─────────────────────
#
# The local-dev stack stores Terraform state REMOTELY in Azure Blob Storage,
# co-located with the rest of the stack inside the working resource group
# `rg-dotnetagent-localdev`. Storage account, container, and the working RG
# itself are bootstrapped via the Azure CLI (out-of-band of Terraform), so
# `terraform destroy` never touches them — state survives:
#   - `setup-local.sh --cleanup` (which only runs `terraform destroy`)
#   - re-running `setup-local.sh` end-to-end (idempotent create-if-absent)
# To wipe everything for real, run `az group delete --name rg-dotnetagent-localdev`.
#
# Idempotent. Runs in BOTH cleanup and main flows.
initialize_backend() {
    local working_rg="$1"
    local location="$2"

    # ── Working RG (out-of-band; Terraform reads it via a data source) ─────
    az group create \
        --name "$working_rg" \
        --location "$location" \
        --tags "managed-by=setup-local" "purpose=local-development" \
        --output none

    local sub_id
    sub_id=$(az account show --query id -o tsv 2>/dev/null)
    if [[ -z "$sub_id" ]]; then
        fail "Could not determine subscription ID"
        exit 1
    fi
    # Hash the subscription ID → 8 hex chars (avoids leaking the raw subId
    # prefix into a globally-visible storage account name).
    local suffix
    suffix=$(printf '%s' "$sub_id" | sha256sum | cut -c1-8)
    local storage_account="stdotnetagentldtf${suffix}"
    storage_account="${storage_account:0:24}"
    local container_name="tfstate"
    local state_key="local-dev.tfstate"

    step "Bootstrapping Terraform state backend"
    ok "Resource group:   $working_rg"
    ok "Storage account:  $storage_account"
    ok "Container / key:  $container_name / $state_key"

    if ! az storage account show --resource-group "$working_rg" --name "$storage_account" >/dev/null 2>&1; then
        az storage account create \
            --resource-group "$working_rg" \
            --name "$storage_account" \
            --location "$location" \
            --sku Standard_LRS \
            --kind StorageV2 \
            --min-tls-version TLS1_2 \
            --allow-blob-public-access false \
            --output none
        ok "Created storage account"
    else
        ok "Storage account already exists"
    fi

    local deployer_oid
    deployer_oid=$(az ad signed-in-user show --query id -o tsv 2>/dev/null)
    if [[ -z "$deployer_oid" ]]; then
        fail "Could not determine deployer object ID"
        exit 1
    fi
    local st_scope
    st_scope=$(az storage account show --name "$storage_account" --resource-group "$working_rg" --query id -o tsv)
    local existing_role
    existing_role=$(az role assignment list \
        --assignee "$deployer_oid" \
        --scope "$st_scope" \
        --role "Storage Blob Data Contributor" \
        --query "[0].id" -o tsv 2>/dev/null || true)
    if [[ -z "$existing_role" ]]; then
        az role assignment create \
            --assignee-object-id "$deployer_oid" \
            --assignee-principal-type User \
            --role "Storage Blob Data Contributor" \
            --scope "$st_scope" --output none 2>/dev/null
        ok "Granted Storage Blob Data Contributor — waiting 30s for RBAC propagation"
        sleep 30
    else
        ok "Storage Blob Data Contributor already assigned"
    fi

    local container_ready=0
    for attempt in 1 2 3 4 5 6; do
        if az storage container show --name "$container_name" --account-name "$storage_account" --auth-mode login >/dev/null 2>&1; then
            container_ready=1; break
        fi
        if az storage container create --name "$container_name" --account-name "$storage_account" --auth-mode login >/dev/null 2>&1; then
            container_ready=1; break
        fi
        if [[ $attempt -lt 6 ]]; then
            echo "    Waiting for RBAC (attempt $attempt/6) — retrying in 15s"
            sleep 15
        fi
    done
    if [[ $container_ready -ne 1 ]]; then
        fail "Failed to create $container_name container"
        exit 1
    fi
    ok "Container ready: $container_name"

    cat > "$TERRAFORM_DIR/backend.hcl" <<EOF
resource_group_name  = "$working_rg"
storage_account_name = "$storage_account"
container_name       = "$container_name"
key                  = "$state_key"
use_azuread_auth     = true
EOF
    ok "Generated backend.hcl"

    # `-reconfigure` discards any prior backend config (e.g. legacy local
    # state from before this script was migrated to remote state) WITHOUT
    # auto-migrating. To migrate a populated local state, run once manually:
    #   terraform -chdir=infra/terraform/local-dev init -migrate-state -backend-config=backend.hcl
    step "Initializing Terraform with remote backend"
    terraform -chdir="$TERRAFORM_DIR" init -reconfigure -backend-config="$TERRAFORM_DIR/backend.hcl" -input=false
    ok "Terraform initialized (remote state in $storage_account/$container_name/$state_key)"
}

# ── Cleanup Mode ────────────────────────────────────────────────────────────

if [[ "${1:-}" == "--cleanup" ]]; then
    # Initialize backend first so `terraform destroy` talks to the remote
    # state. State RG / storage / container are NOT destroyed.
    initialize_backend "$WORKING_RG" "$WORKING_LOCATION"

    step "Destroying Terraform resources"
    terraform -chdir="$TERRAFORM_DIR" destroy -auto-approve -input=false
    ok "Terraform resources destroyed (state backend in ${WORKING_RG} preserved)"

    step "Removing generated appsettings.Local.json files"
    ALL_COMPONENTS=("${TEMPLATE_COMPONENTS[@]}" "${STATIC_COMPONENTS[@]}")
    for component in "${ALL_COMPONENTS[@]}"; do
        settings_file="$REPO_ROOT/src/$component/appsettings.Local.json"
        if [[ -f "$settings_file" ]]; then
            rm -f "$settings_file"
            ok "Removed src/$component/appsettings.Local.json"
        fi
    done

    credentials_file="$REPO_ROOT/local-dev-credentials.txt"
    if [[ -f "$credentials_file" ]]; then
        rm -f "$credentials_file"
        ok "Removed local-dev-credentials.txt"
    fi

    echo -e "\n\033[32mCleanup complete.\033[0m"
    exit 0
fi

# ── Prerequisites ───────────────────────────────────────────────────────────

step "Checking prerequisites"

missing=()
command_exists dotnet    || missing+=("dotnet")
command_exists az        || missing+=("az (Azure CLI)")
command_exists terraform || missing+=("terraform")
# python3 is used to parse `az account show` output, terraform JSON outputs,
# and to substitute placeholders in appsettings templates without invoking
# sed (which mis-handles the `&`, `/`, and `$` characters in passwords/URIs).
command_exists python3   || missing+=("python3")

if [[ ${#missing[@]} -gt 0 ]]; then
    fail "Missing required tools: ${missing[*]}"
    echo "  Install them and re-run this script."
    exit 1
fi
ok "dotnet, az, terraform, python3 found"

# ── Azure Login Check ──────────────────────────────────────────────────────

step "Checking Azure login"
if ! az_account=$(az account show 2>&1); then
    fail "Not logged in to Azure. Run 'az login' first."
    exit 1
fi
az_user=$(echo "$az_account" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d['user']['name'])" 2>/dev/null || echo "unknown")
az_sub=$(echo "$az_account" | python3 -c "import sys,json; d=json.load(sys.stdin); print(d['name'])" 2>/dev/null || echo "unknown")
ok "Logged in as $az_user (subscription: $az_sub)"

# ── Compute Resource Group Name ─────────────────────────────────────────────

step "Computing resource group name"
export TF_VAR_resource_group_name="$WORKING_RG"
ok "Resource group: $TF_VAR_resource_group_name"

# ── Bootstrap Terraform state backend + init ────────────────────────────────
# initialize_backend also creates the working RG (idempotent) since the state
# storage account lives there — the Terraform stack reads the RG via a `data`
# source, so it's never in TF state and `terraform destroy` won't touch it.
initialize_backend "$WORKING_RG" "$WORKING_LOCATION"

# ── Purge soft-deleted resources ────────────────────────────────────────────
# Cognitive Services accounts and Key Vaults use soft-delete by default. If a
# prior `setup-local.sh --cleanup` ran (or terraform destroy), the soft-deleted
# resources block re-creation with `FlagMustBeSetForRestore` (HTTP 409). Purge
# them so the next `terraform apply` can recreate cleanly.
step "Purging soft-deleted resources (if any)"

soft_cog=$(az cognitiveservices account list-deleted \
    --query "[?contains(id, '$TF_VAR_resource_group_name')].name" -o tsv 2>/dev/null || true)
if [[ -n "$soft_cog" ]]; then
    while IFS= read -r acct; do
        [[ -z "$acct" ]] && continue
        # Recover the deleted account's location from its full ID so we don't
        # have to hardcode a region (the same RG name can host accounts in
        # different regions across re-deploys).
        acct_loc=$(az cognitiveservices account list-deleted \
            --query "[?name=='$acct'].location | [0]" -o tsv 2>/dev/null || true)
        [[ -z "$acct_loc" ]] && continue
        az cognitiveservices account purge --location "$acct_loc" \
            --resource-group "$TF_VAR_resource_group_name" --name "$acct" >/dev/null 2>&1 || true
        ok "Purged Cognitive Services: $acct ($acct_loc)"
    done <<< "$soft_cog"
fi

soft_kv=$(az keyvault list-deleted \
    --query "[?properties.vaultId && contains(properties.vaultId, '$TF_VAR_resource_group_name')].name" -o tsv 2>/dev/null || true)
if [[ -n "$soft_kv" ]]; then
    while IFS= read -r kv; do
        [[ -z "$kv" ]] && continue
        az keyvault purge --name "$kv" --no-wait >/dev/null 2>&1 || true
        ok "Purged Key Vault: $kv"
    done <<< "$soft_kv"
fi

if [[ -z "$soft_cog" && -z "$soft_kv" ]]; then
    ok "No soft-deleted resources to purge"
fi

# ── Delete orphan Entra test users ──────────────────────────────────────────
#
# Terraform owns these test users. They live in tenant-wide Entra, so a
# `terraform destroy` followed by `terraform apply` (or any state loss) can
# leave the UPN in Entra without a matching state entry — the next apply
# would then 409 with "user already exists".
#
# To avoid that we delete ONLY genuine orphans: UPNs that exist in Entra
# but have no corresponding `azuread_user.test["<key>"]` entry in Terraform
# state. Users that are already managed by Terraform are left alone, so
# repeat runs of setup-local are a no-op for them — no recreate, no
# password rotation, no invalidated browser sessions.
step "Checking for orphan Entra test users"

tenant_domain=$(az rest --method GET --url 'https://graph.microsoft.com/v1.0/domains' \
    --query "value[?isDefault].id | [0]" -o tsv 2>/dev/null || true)
if [[ -z "$tenant_domain" ]]; then
    fail "Could not determine default tenant domain via Graph"
    exit 1
fi
ok "Tenant default domain: $tenant_domain"

# Snapshot the list of test users currently in Terraform state so we can
# tell "managed by TF" (skip) from "true orphan" (delete). Single remote-
# state read; subsequent UPN lookups are local string matches.
managed_user_keys=""
if tf_state_lines=$(terraform -chdir="$TERRAFORM_DIR" state list 2>/dev/null); then
    # Extract the bracketed key from lines like:
    #   module.entra.azuread_user.test["anna"]
    managed_user_keys=$(echo "$tf_state_lines" | grep -oE 'azuread_user\.test\["[^"]+"\]' | sed -E 's/.*\["([^"]+)"\].*/\1/' || true)
fi

# Mirror of `var.test_users` defaults in
# infra/terraform/modules/entra/v1/variables.tf — keep in sync.
# Use parallel arrays (not associative) to preserve declaration order on
# bash 4 / 5 — declared assoc-array iteration order is unspecified.
# The `-local` suffix matches `mail_nickname_suffix` passed from
# infra/terraform/local-dev/main.tf so we look up the SAME UPNs Terraform
# will create. The Full Azure Track uses no suffix, so its `emma.wilson@`
# users are not deleted by this script.
TEST_USER_KEYS=(emma james sarah david lisa mike anna tom)
TEST_USER_NICKS=(emma.wilson-local james.chen-local sarah.miller-local david.park-local lisa.torres-local mike.johnson-local anna.roberts-local tom.garcia-local)

# Build JSON map of {key: oid} for users that exist.
deleted_count=0
skipped_count=0
for i in "${!TEST_USER_KEYS[@]}"; do
    key="${TEST_USER_KEYS[$i]}"
    upn="${TEST_USER_NICKS[$i]}@${tenant_domain}"
    oid=$(az ad user show --id "$upn" --query id -o tsv 2>/dev/null || true)
    if [[ -z "$oid" ]]; then
        continue
    fi
    if echo "$managed_user_keys" | grep -qx "$key"; then
        # Already in TF state — leave it alone. terraform apply will be a no-op.
        skipped_count=$((skipped_count + 1))
        continue
    fi
    if ! az ad user delete --id "$upn" 2>/dev/null; then
        fail "Failed to delete orphan user $upn (object id $oid)"
        exit 1
    fi
    ok "Deleted orphan: $upn"
    deleted_count=$((deleted_count + 1))
done
if [[ $deleted_count -eq 0 ]]; then
    if [[ $skipped_count -gt 0 ]]; then
        ok "$skipped_count test user(s) already managed by Terraform — leaving as-is"
    else
        ok "No orphan test users found"
    fi
fi

# ── Terraform Apply ─────────────────────────────────────────────────────────

step "Applying Terraform (this may take a few minutes)"
terraform -chdir="$TERRAFORM_DIR" apply -auto-approve -input=false
ok "Terraform apply complete"

# ── Retrieve Outputs ────────────────────────────────────────────────────────

step "Retrieving Terraform outputs"
outputs_json=$(terraform -chdir="$TERRAFORM_DIR" output -json)
foundry_project_endpoint=$(echo "$outputs_json" | python3 -c "import sys,json; print(json.load(sys.stdin)['foundry_project_endpoint']['value'])")
chat_deployment_name=$(echo "$outputs_json"    | python3 -c "import sys,json; print(json.load(sys.stdin)['chat_deployment_name']['value'])")
embedding_deployment_name=$(echo "$outputs_json" | python3 -c "import sys,json; print(json.load(sys.stdin)['embedding_deployment_name']['value'])")
tenant_id=$(echo "$outputs_json"               | python3 -c "import sys,json; print(json.load(sys.stdin)['tenant_id']['value'])")
bff_client_id=$(echo "$outputs_json"           | python3 -c "import sys,json; print(json.load(sys.stdin)['bff_client_id']['value'])")
customer_map_json=$(echo "$outputs_json"       | python3 -c "import sys,json; print(json.load(sys.stdin)['customer_map_json']['value'])")
test_user_upns_json=$(echo "$outputs_json"     | python3 -c "import sys,json; print(json.dumps(json.load(sys.stdin)['test_user_upns']['value']))")
ok "Foundry project endpoint: $foundry_project_endpoint"
ok "Chat deployment: $chat_deployment_name"
ok "Embedding deployment: $embedding_deployment_name"
ok "Tenant ID: $tenant_id"
ok "BFF SPA client ID: $bff_client_id"

# Pull sensitive password output via terraform's targeted -json mode so we
# never write it to disk except inside the gitignored credentials file below.
test_user_passwords_json=$(terraform -chdir="$TERRAFORM_DIR" output -json test_user_passwords)

# ── Write test-user credentials to a gitignored file ───────────────
#
# Passwords are stable across `setup-local` runs — the random_pet /
# random_integer resources backing them stay in Terraform state, and we
# only delete genuine orphans (UPNs not in TF state) before applying.
# A password only changes after a `setup-local --cleanup` (or a manual
# `terraform destroy`) followed by a fresh setup. Lab students need to
# copy individual passwords back into the Blazor sign-in dialog as they
# exercise different customer scenarios; printing them to the terminal
# isn't practical, so we write them to a per-clone gitignored file at the
# repo root. The file is rewritten in full on every apply so it always
# reflects the live passwords.
step "Writing test-user credentials"

credentials_path="$REPO_ROOT/local-dev-credentials.txt"
credentials_relative="${credentials_path#$REPO_ROOT/}"

TEST_USER_UPNS_JSON="$test_user_upns_json" \
TEST_USER_PASSWORDS_JSON="$test_user_passwords_json" \
CREDENTIALS_PATH="$credentials_path" \
TENANT_ID_OUT="$tenant_id" \
python3 - <<'PY'
import datetime, json, os
upns      = json.loads(os.environ['TEST_USER_UPNS_JSON'])
passwords = json.loads(os.environ['TEST_USER_PASSWORDS_JSON'])
dst       = os.environ['CREDENTIALS_PATH']
tenant    = os.environ['TENANT_ID_OUT']
lines = [
    "# Local-dev test-user credentials",
    f"# Generated: {datetime.datetime.now().astimezone().strftime('%Y-%m-%d %H:%M:%S %z')}",
    f"# Tenant:    {tenant}",
    "# WARNING:   gitignored — do not commit. Passwords persist across setup-local runs;",
    "#            they only change after a --cleanup followed by a fresh setup.",
    "",
    f"{'key':<7}  {'upn':<50}  password",
    f"{'---':<7}  {'---':<50}  ---",
]
for key in sorted(upns.keys()):
    lines.append(f"{key:<7}  {upns[key]:<50}  {passwords[key]}")
with open(dst, 'w', encoding='utf-8') as f:
    f.write('\n'.join(lines) + '\n')
PY
ok "Wrote $credentials_relative"

# ── Generate appsettings.Local.json from Templates ──────────────────────────

step "Generating appsettings.Local.json files"

# Substitute placeholders without invoking sed (which would mis-handle
# the ampersands, slashes, and `$` characters inside generated passwords
# and URIs). Use Python for literal string replacement.
substitute_template() {
    local template_path="$1"
    local output_path="$2"
    python3 - "$template_path" "$output_path" <<'PY'
import os, sys
src, dst = sys.argv[1], sys.argv[2]
with open(src, 'r', encoding='utf-8') as f:
    content = f.read()
mapping = {
    '{{FOUNDRY_PROJECT_ENDPOINT}}': os.environ['FOUNDRY_PROJECT_ENDPOINT'],
    '{{CHAT_DEPLOYMENT_NAME}}':      os.environ['CHAT_DEPLOYMENT_NAME'],
    '{{EMBEDDING_DEPLOYMENT_NAME}}': os.environ['EMBEDDING_DEPLOYMENT_NAME'],
    '{{TENANT_ID}}':                 os.environ['TENANT_ID'],
    '{{BFF_CLIENT_ID}}':             os.environ['BFF_CLIENT_ID'],
    '{{CUSTOMER_MAP_JSON}}':         os.environ['CUSTOMER_MAP_JSON'],
}
for k, v in mapping.items():
    content = content.replace(k, v)
with open(dst, 'w', encoding='utf-8') as f:
    f.write(content)
PY
}

export FOUNDRY_PROJECT_ENDPOINT="$foundry_project_endpoint"
export CHAT_DEPLOYMENT_NAME="$chat_deployment_name"
export EMBEDDING_DEPLOYMENT_NAME="$embedding_deployment_name"
export TENANT_ID="$tenant_id"
export BFF_CLIENT_ID="$bff_client_id"
export CUSTOMER_MAP_JSON="$customer_map_json"

for component in "${TEMPLATE_COMPONENTS[@]}"; do
    template_path="$REPO_ROOT/src/$component/appsettings.Local.json.template"
    # blazor-ui is a WASM SPA — the browser fetches configuration from
    # wwwroot/appsettings.Local.json over HTTP. Every other component reads
    # from the project root at process startup.
    if [[ "$component" == "blazor-ui" ]]; then
        output_path="$REPO_ROOT/src/$component/wwwroot/appsettings.Local.json"
    else
        output_path="$REPO_ROOT/src/$component/appsettings.Local.json"
    fi

    if [[ ! -f "$template_path" ]]; then
        warn "Template not found: $template_path — skipping"
        continue
    fi

    substitute_template "$template_path" "$output_path"
    ok "Generated ${output_path#$REPO_ROOT/}"
done

for component in "${STATIC_COMPONENTS[@]}"; do
    template_path="$REPO_ROOT/src/$component/appsettings.Local.json.template"
    if [[ "$component" == "blazor-ui" ]]; then
        output_path="$REPO_ROOT/src/$component/wwwroot/appsettings.Local.json"
    else
        output_path="$REPO_ROOT/src/$component/appsettings.Local.json"
    fi

    if [[ ! -f "$template_path" ]]; then
        warn "Template not found: $template_path — skipping"
        continue
    fi

    cp "$template_path" "$output_path"
    ok "Generated ${output_path#$REPO_ROOT/}"
done

# ── Summary ─────────────────────────────────────────────────────────────────

echo ""
echo -e "\033[32m============================================================\033[0m"
echo -e "\033[32m  Local Dev Setup Complete\033[0m"
echo -e "\033[32m============================================================\033[0m"
echo ""
echo "  Sign-in credentials for the 8 test users have been written to:"
echo -e "    \033[33m${credentials_relative}\033[0m"
echo -e "    \033[90m(gitignored — passwords persist across runs; --cleanup rotates them)\033[0m"
echo ""
echo "  Port Map:"
for port in $(echo "${!PORT_MAP[@]}" | tr ' ' '\n' | sort -n); do
    printf "    %s  %s\n" "$port" "${PORT_MAP[$port]}"
done
echo ""
echo "  Run all services:"
echo -e "    \033[33mdotnet run --project src/AppHost\033[0m"
echo ""
echo "  Or run individual components:"
echo -e "    \033[33mdotnet run --project src/crm-api --environment Local\033[0m"
echo ""
echo "  To tear down:"
echo -e "    \033[33m./infra/setup-local.sh --cleanup\033[0m"
echo ""
