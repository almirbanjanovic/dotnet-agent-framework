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

# ── Cleanup Mode ────────────────────────────────────────────────────────────

if [[ "${1:-}" == "--cleanup" ]]; then
    step "Destroying Terraform resources"
    terraform -chdir="$TERRAFORM_DIR" destroy -auto-approve -input=false
    ok "Terraform resources destroyed"

    step "Removing generated appsettings.Local.json files"
    ALL_COMPONENTS=("${TEMPLATE_COMPONENTS[@]}" "${STATIC_COMPONENTS[@]}")
    for component in "${ALL_COMPONENTS[@]}"; do
        settings_file="$REPO_ROOT/src/$component/appsettings.Local.json"
        if [[ -f "$settings_file" ]]; then
            rm -f "$settings_file"
            ok "Removed src/$component/appsettings.Local.json"
        fi
    done

    echo -e "\n\033[32mCleanup complete.\033[0m"
    exit 0
fi

# ── Prerequisites ───────────────────────────────────────────────────────────

step "Checking prerequisites"

missing=()
command_exists dotnet    || missing+=("dotnet")
command_exists az        || missing+=("az (Azure CLI)")
command_exists terraform || missing+=("terraform")

if [[ ${#missing[@]} -gt 0 ]]; then
    fail "Missing required tools: ${missing[*]}"
    echo "  Install them and re-run this script."
    exit 1
fi
ok "dotnet, az, terraform found"

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
export TF_VAR_resource_group_name="rg-dotnetagent-localdev"
ok "Resource group: $TF_VAR_resource_group_name"

# ── Terraform Init ──────────────────────────────────────────────────────────

step "Initializing Terraform"
terraform -chdir="$TERRAFORM_DIR" init -input=false
ok "Terraform initialized"

# ── Terraform Apply ─────────────────────────────────────────────────────────

step "Applying Terraform (this may take a few minutes)"
terraform -chdir="$TERRAFORM_DIR" apply -auto-approve -input=false
ok "Terraform apply complete"

# ── Retrieve Outputs ────────────────────────────────────────────────────────

step "Retrieving Terraform outputs"
outputs_json=$(terraform -chdir="$TERRAFORM_DIR" output -json)
foundry_endpoint=$(echo "$outputs_json"        | python3 -c "import sys,json; print(json.load(sys.stdin)['foundry_endpoint']['value'])")
chat_deployment_name=$(echo "$outputs_json"    | python3 -c "import sys,json; print(json.load(sys.stdin)['chat_deployment_name']['value'])")
embedding_deployment_name=$(echo "$outputs_json" | python3 -c "import sys,json; print(json.load(sys.stdin)['embedding_deployment_name']['value'])")
tenant_id=$(echo "$outputs_json"               | python3 -c "import sys,json; print(json.load(sys.stdin)['tenant_id']['value'])")
bff_client_id=$(echo "$outputs_json"           | python3 -c "import sys,json; print(json.load(sys.stdin)['bff_client_id']['value'])")
customer_map_json=$(echo "$outputs_json"       | python3 -c "import sys,json; print(json.load(sys.stdin)['customer_map_json']['value'])")
ok "Foundry endpoint: $foundry_endpoint"
ok "Chat deployment: $chat_deployment_name"
ok "Embedding deployment: $embedding_deployment_name"
ok "Tenant ID: $tenant_id"
ok "BFF SPA client ID: $bff_client_id"

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
    '{{FOUNDRY_ENDPOINT}}':          os.environ['FOUNDRY_ENDPOINT'],
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

export FOUNDRY_ENDPOINT="$foundry_endpoint"
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
echo "  Sign in to the Blazor UI as one of these test users"
echo "  (saved in your Entra tenant; passwords printed once):"
python3 - <<PY
import json, os
upns      = json.loads(os.popen("terraform -chdir='$TERRAFORM_DIR' output -json test_user_upns").read())
passwords = json.loads(os.popen("terraform -chdir='$TERRAFORM_DIR' output -json test_user_passwords").read())
imported  = json.loads(os.popen("terraform -chdir='$TERRAFORM_DIR' output -json imported_user_keys").read()) or []
for key in sorted(upns.keys()):
    if key in imported:
        print(f"    {key:<7} {upns[key]:<50} <imported \u2014 use password from prior setup-local run>")
    else:
        print(f"    {key:<7} {upns[key]:<50} {passwords[key]}")
if imported:
    print()
    print(f"  Note: {len(imported)} user(s) already existed in this tenant and were imported")
    print( "  into terraform state. Their passwords were NOT reset. If you don't have the")
    print( "  password from the original setup-local run, reset it in the Azure portal under:")
    print( "    Microsoft Entra ID > Users > <user> > Reset password.")
PY
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
