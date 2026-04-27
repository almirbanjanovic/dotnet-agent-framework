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
TERRAFORM_DIR="infra/terraform/local-dev"

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
)

# Templates that are static (no placeholder replacement needed)
STATIC_COMPONENTS=(
    "crm-api"
    "crm-mcp"
    "bff-api"
    "blazor-ui"
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
username=$(whoami | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9')
export TF_VAR_resource_group_name="rg-dotnetagent-localdev-$username"
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
foundry_api_key=$(echo "$outputs_json"         | python3 -c "import sys,json; print(json.load(sys.stdin)['foundry_api_key']['value'])")
chat_deployment_name=$(echo "$outputs_json"    | python3 -c "import sys,json; print(json.load(sys.stdin)['chat_deployment_name']['value'])")
embedding_deployment_name=$(echo "$outputs_json" | python3 -c "import sys,json; print(json.load(sys.stdin)['embedding_deployment_name']['value'])")
ok "Foundry endpoint: $foundry_endpoint"
ok "Chat deployment: $chat_deployment_name"
ok "Embedding deployment: $embedding_deployment_name"

# ── Generate appsettings.Local.json from Templates ──────────────────────────

step "Generating appsettings.Local.json files"

for component in "${TEMPLATE_COMPONENTS[@]}"; do
    template_path="$REPO_ROOT/src/$component/appsettings.Local.json.template"
    output_path="$REPO_ROOT/src/$component/appsettings.Local.json"

    if [[ ! -f "$template_path" ]]; then
        warn "Template not found: $template_path — skipping"
        continue
    fi

    sed \
        -e "s|{{FOUNDRY_ENDPOINT}}|$foundry_endpoint|g" \
        -e "s|{{FOUNDRY_API_KEY}}|$foundry_api_key|g" \
        -e "s|{{CHAT_DEPLOYMENT_NAME}}|$chat_deployment_name|g" \
        -e "s|{{EMBEDDING_DEPLOYMENT_NAME}}|$embedding_deployment_name|g" \
        "$template_path" > "$output_path"

    ok "Generated src/$component/appsettings.Local.json"
done

for component in "${STATIC_COMPONENTS[@]}"; do
    template_path="$REPO_ROOT/src/$component/appsettings.Local.json.template"
    output_path="$REPO_ROOT/src/$component/appsettings.Local.json"

    if [[ ! -f "$template_path" ]]; then
        warn "Template not found: $template_path — skipping"
        continue
    fi

    cp "$template_path" "$output_path"
    ok "Generated src/$component/appsettings.Local.json"
done

# ── Summary ─────────────────────────────────────────────────────────────────

echo ""
echo -e "\033[32m============================================================\033[0m"
echo -e "\033[32m  Local Dev Setup Complete\033[0m"
echo -e "\033[32m============================================================\033[0m"
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
