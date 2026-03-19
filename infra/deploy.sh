#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════════
# .NET Agent Framework — Lab 1 Deploy
#
# Mirrors the CI/CD workflow: opens resource firewalls, runs terraform
# init/validate/plan/apply, seeds data, then locks firewalls again.
#
# Usage:  ./deploy.sh
# ═══════════════════════════════════════════════════════════════════════════════

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TERRAFORM_DIR="$SCRIPT_DIR/terraform"

# ── Helpers ──────────────────────────────────────────────────────────────────
C='\033[36m' G='\033[32m' R='\033[31m' D='\033[90m' Y='\033[33m' W='\033[0m'

# Wrap az CLI to strip Windows \r\n from stdout (WSL may call Windows az.cmd)
az() {
    local out rc
    out=$(command az "$@") && rc=$? || rc=$?
    [[ -n "$out" ]] && printf '%s\n' "$out" | tr -d '\r'
    return $rc
}

banner() {
    echo -e ""
    echo -e "  ${C}╔═══════════════════════════════════════════════════════════╗${W}"
    echo -e "  ${C}║                                                           ║${W}"
    echo -e "  ${C}║   .NET Agent Framework — Lab 1 Deploy                     ║${W}"
    echo -e "  ${C}║                                                           ║${W}"
    echo -e "  ${C}║   This script deploys all Azure infrastructure:           ║${W}"
    echo -e "  ${C}║                                                           ║${W}"
    echo -e "  ${C}║     0. Agent Identity SP (msgraph provider credentials)   ║${W}"
    echo -e "  ${C}║     1. Open resource firewalls (deployer IP allowlist)    ║${W}"
    echo -e "  ${C}║     2. terraform init (backend + Entra user import)       ║${W}"
    echo -e "  ${C}║     3. terraform validate (syntax check)                  ║${W}"
    echo -e "  ${C}║     4. terraform plan (preview changes)                   ║${W}"
    echo -e "  ${C}║     5. terraform apply (provision resources)              ║${W}"
    echo -e "  ${C}║     6. Seed CRM data (CSV -> Cosmos DB via AKS pod)       ║${W}"
    echo -e "  ${C}║     7. Link Entra users -> Customers (identity mapping)   ║${W}"
    echo -e "  ${C}║     *  Close resource firewalls (always runs on exit)     ║${W}"
    echo -e "  ${C}║                                                           ║${W}"
    echo -e "  ${C}╚═══════════════════════════════════════════════════════════╝${W}"
    echo -e ""
}

phase() {
    local num="$1" title="$2"
    local w=59
    local border=$(printf '=%.0s' $(seq 1 $w))

    # Build art lines based on phase number
    local lines=()
    case "$num" in
        0) lines=(
            "                                                           "
            "       .---.     Phase 0                                   "
            "      / SP  \\    Agent Identity Service Principal          "
            "      \\____/                                               "
            "                                                           "
        ) ;;
        1) lines=(
            "                                                           "
            "      +-----+   Phase 1                                    "
            "      | F W |   Open Resource Firewalls                    "
            "      +-----+                                              "
            "                                                           "
        ) ;;
        2) lines=(
            "                                                           "
            "      +-----+   Phase 2                                    "
            "      | T F |   Terraform Init                             "
            "      +-----+                                              "
            "                                                           "
        ) ;;
        3) lines=(
            "                                                           "
            "      +-----+   Phase 3                                    "
            "      |  *  |   Terraform Validate                         "
            "      +-----+                                              "
            "                                                           "
        ) ;;
        4) lines=(
            "                                                           "
            "      +-----+   Phase 4                                    "
            "      | <+> |   Terraform Plan                             "
            "      +-----+                                              "
            "                                                           "
        ) ;;
        5) lines=(
            "                                                           "
            "      +-----+   Phase 5                                    "
            "      | >>> |   Terraform Apply                            "
            "      +-----+                                              "
            "                                                           "
        ) ;;
        6) lines=(
            "                                                           "
            "      +-----+   Phase 6                                    "
            "      | DB  |   Seed CRM Data                              "
            "      +-----+                                              "
            "                                                           "
        ) ;;
        7) lines=(
            "                                                           "
            "      +-----+   Phase 7                                    "
            "      | <-> |   Link Entra Users to Customers              "
            "      +-----+                                              "
            "                                                           "
        ) ;;
    esac

    echo ""
    echo -e "  ${C}+${border}+${W}"
    for line in "${lines[@]}"; do
        echo -e "  ${C}|${W}${line}${C}|${W}"
    done
    echo -e "  ${C}+${border}+${W}"
    echo ""
}

step()  { echo -e "  → $1"; }
done_() { echo -e "    ${G}✓ $1${W}"; }
info_() { echo -e "    ${D}ℹ $1${W}"; }

wait_progress() {
    local secs="$1" msg="$2" bar_width=30
    echo ""
    for (( i=1; i<=secs; i++ )); do
        local pct=$((i * 100 / secs))
        local filled=$((i * bar_width / secs))
        local empty=$((bar_width - filled))
        local bar=""
        for (( j=0; j<filled; j++ )); do bar+="█"; done
        for (( j=0; j<empty; j++ )); do bar+="░"; done
        printf "\r    [%s] %3d%%  %s (%d/%ds)" "$bar" "$pct" "$msg" "$i" "$secs"
        sleep 1
    done
    local full_bar=""
    for (( j=0; j<bar_width; j++ )); do full_bar+="█"; done
    printf "\r    ${G}[%s] 100%%  %s              ${W}\n" "$full_bar" "$msg"
    echo ""
}

phase_summary() {
    local num="$1"; local next_desc="$2"; shift 2
    
    # Build content lines and find max width
    local header=" Phase $num complete "
    local lines=()
    while [[ $# -gt 0 ]]; do
        lines+=("  $1: $2")
        shift 2
    done
    
    local max_len=${#header}
    for line in "${lines[@]}"; do
        (( ${#line} > max_len )) && max_len=${#line}
    done
    local box_width=$((max_len + 2))
    
    local top_fill=$(printf '─%.0s' $(seq 1 $((box_width - ${#header}))))
    local bot_fill=$(printf '─%.0s' $(seq 1 $box_width))
    
    echo ""
    echo -e "    ${G}┌${header}${top_fill}┐${W}"
    for line in "${lines[@]}"; do
        local pad_len=$((box_width - ${#line}))
        local pad=$(printf ' %.0s' $(seq 1 $pad_len))
        echo -e "    ${G}│${W}${line}${pad}${G}│${W}"
    done
    echo -e "    ${G}└${bot_fill}┘${W}"
    if [[ -n "$next_desc" ]]; then
        echo -e "    ${D}Next:${W} ${C}${next_desc}${W}"
    fi
    if [[ "$AUTO_MODE" != "true" ]]; then
        read -p "    Continue? (Y/n) " response
        if [[ "$response" == "n" || "$response" == "N" ]]; then
            echo -e "    ${Y}Stopped by user.${W}"
            exit 0
        fi
    fi
}

read_hcl_value() {
    local file="$1" key="$2"
    grep -E "^\s*${key}\s*=" "$file" | head -1 | sed 's/.*=\s*"\([^"]*\)".*/\1/'
}

# ── Select environment ───────────────────────────────────────────────────────────

mapfile -t tfvars_files < <(find "$TERRAFORM_DIR" -maxdepth 1 -name '*.tfvars' ! -name 'example.tfvars' -printf '%f\n' | sort)

if [[ ${#tfvars_files[@]} -eq 0 ]]; then
    echo "No .tfvars files found — run init.sh first."; exit 1
fi

banner

# ── Run mode ─────────────────────────────────────────────────────────────────
echo -e "    ${D}How would you like to run this deployment?${W}"
echo ""
echo -e "      ${C}1. Interactive  — pause between phases for review${W}"
echo -e "      ${C}2. Auto         — run all phases without stopping${W}"
echo ""
read -p "    Select [1-2, or press Enter for interactive]: " mode_input
AUTO_MODE=false
if [[ "$mode_input" == "2" ]]; then
    AUTO_MODE=true
    echo ""
    echo -e "    ${G}✓ Auto mode — will run all phases without pausing.${W}"
    echo -e "      ${D}(Script will still stop on errors.)${W}"
else
    echo ""
    echo -e "    ${G}✓ Interactive mode — will pause between each phase.${W}"
fi
echo ""

# ── Azure login ───────────────────────────────────────────────────────────────────
az config set core.login_experience_v2=off 2>/dev/null
az config set core.enable_broker_on_windows=false 2>/dev/null
echo -e "    ${D}Signing in to Azure — select the correct account in the browser.${W}"
echo ""
# Clear stale MSAL token cache to prevent CAE 'TokenCreatedWithOutdatedPolicies' errors.
# Do NOT use --scope https://graph.microsoft.com/.default — it acquires a broad
# delegated token including Directory.AccessAsUser.All, which the Entra Agent ID
# API explicitly rejects. Let the msgraph provider acquire its own scoped tokens.
az account clear 2>/dev/null
az login >/dev/null

# Ensure we're on the correct tenant and export for DefaultAzureCredential
export AZURE_TENANT_ID=$(az account show --query tenantId -o tsv)
done_ "Signed in (tenant: $AZURE_TENANT_ID)"

# Disable Continuous Access Evaluation for Terraform — the Go SDKs acquires
# their own tokens which get CAE-challenged by aggressive org policies.
# ARM_DISABLE_CAE / AZURE_DISABLE_CAE → azurerm + azapi providers
# HAMILTON_DISABLE_CAE → azuread provider (uses manicminer/hamilton SDK)
export ARM_DISABLE_CAE=true
export AZURE_DISABLE_CAE=true
export HAMILTON_DISABLE_CAE=true

# ═══════════════════════════════════════════════════════════════════════════════
# Agent Identity — Service Principal Setup
#
# WHY: The Entra Agent ID API requires app-only tokens (client credentials).
#      Azure CLI's delegated tokens always include Directory.AccessAsUser.All,
#      which the Agent ID API explicitly blocks. This section creates a
#      service principal that Terraform's msgraph provider uses instead.
#
#      ┌──────────────┐     delegated token      ┌─────────────────┐
#      │   Azure CLI  │ ──────────────────────── │  Agent ID API   │
#      │  (az login)  │  includes Directory.*    │   ❌ REJECTED   │
#      └──────────────┘                          └─────────────────┘
#
#      ┌──────────────┐     app-only token       ┌─────────────────┐
#      │  Service     │ ──────────────────────── │  Agent ID API   │
#      │  Principal   │  Application.ReadWrite.  │   ✅ ACCEPTED   │
#      └──────────────┘  All (no Directory.*)    └─────────────────┘
#
# ═══════════════════════════════════════════════════════════════════════════════

phase 0 "Agent Identity — Service Principal"
info_ "The Entra Agent ID API requires app-only tokens (client credentials)."
info_ "Azure CLI tokens include Directory.AccessAsUser.All, which is blocked."
info_ "This phase creates a service principal for Terraform's msgraph provider."
info_ ""
info_ "  az login token ──▶ Directory.AccessAsUser.All ──▶ ❌ Agent ID API"
info_ "  SP app-only    ──▶ AgentIdentity.* perms only ──▶ ✅ Agent ID API"

TENANT_ID=$(az account show --query tenantId -o tsv)
SUBSCRIPTION_ID=$(az account show --query id -o tsv)

# ── Step 1: Find or create service principal ─────────────────────────────────
REPO_NAME=$(basename "$(git rev-parse --show-toplevel 2>/dev/null)" 2>/dev/null || echo "dotnet-agent-framework")
SP_TEMP_SECRET=""

step "Looking for existing service principal..."

SP_CLIENT_ID=""
SP_APP_NAME=""
for name in "github-actions-${REPO_NAME}" "terraform-msgraph-${REPO_NAME}"; do
    SP_CLIENT_ID=$(az ad app list --display-name "$name" --query "[0].appId" -o tsv 2>/dev/null || true)
    if [[ -n "$SP_CLIENT_ID" ]]; then
        SP_APP_NAME="$name"
        done_ "Found: $SP_APP_NAME ($SP_CLIENT_ID)"
        break
    fi
done

if [[ -z "$SP_CLIENT_ID" ]]; then
    SP_APP_NAME="terraform-msgraph-${REPO_NAME}"
    echo -e "    ${D}No existing SP found. Creating '$SP_APP_NAME'...${W}"
    SP_CLIENT_ID=$(az ad app create --display-name "$SP_APP_NAME" --query appId -o tsv 2>/dev/null || true)
    if [[ -n "$SP_CLIENT_ID" ]]; then
        az ad sp create --id "$SP_CLIENT_ID" 2>/dev/null || true
        done_ "Created: $SP_APP_NAME ($SP_CLIENT_ID)"
    else
        echo -e "    ${R}✗ Could not create app registration${W}"
        echo -e "      ${R}Agent Identity blueprints will not be created.${W}"
        echo -e "      ${D}Other resources will deploy normally.${W}"
    fi
fi

# ── Step 2: Grant Graph API permission ───────────────────────────────────────
if [[ -n "$SP_CLIENT_ID" ]]; then
    step "Ensuring Agent Identity permissions (Graph API)..."

    # Permissions needed for Agent Identity Blueprint operations
    declare -A REQUIRED_PERMS=(
        ["Application.ReadWrite.All"]="1bfefb4e-e0b5-418b-a88f-73c46d2cc8e9"
        ["AgentIdentityBlueprint.Create"]="ea4b2453-ad2d-4d94-9155-10d5d9493ce9"
        ["AgentIdentityBlueprint.ReadWrite.All"]="7fddd33b-d884-4ec0-8696-72cff90ff825"
        ["AgentIdentityBlueprint.AddRemoveCreds.All"]="0510736e-bdfb-4b37-9a1f-89b4a074763a"
        ["AgentIdentityBlueprintPrincipal.Create"]="8959696d-d07e-4916-9b1e-3ba9ce459161"
        ["AgentIdentityBlueprintPrincipal.ReadWrite.All"]="3bc933bc-8b4d-4cb6-ac49-b73774299250"
    )

    NEW_PERMS_ADDED=false
    EXISTING_PERMS=$(az ad app permission list --id "$SP_CLIENT_ID" \
        --query "[?resourceAppId=='00000003-0000-0000-c000-000000000000'].resourceAccess[].id" \
        -o tsv 2>/dev/null || true)

    for PERM_NAME in "${!REQUIRED_PERMS[@]}"; do
        PERM_ID="${REQUIRED_PERMS[$PERM_NAME]}"
        if ! echo "$EXISTING_PERMS" | grep -qF "$PERM_ID"; then
            az ad app permission add --id "$SP_CLIENT_ID" \
                --api "00000003-0000-0000-c000-000000000000" \
                --api-permissions "${PERM_ID}=Role" 2>/dev/null || true
            done_ "$PERM_NAME added"
            NEW_PERMS_ADDED=true
        fi
    done
    if ! $NEW_PERMS_ADDED; then
        done_ "All permissions already exist"
    fi

    step "Applying admin consent..."
    az ad app permission admin-consent --id "$SP_CLIENT_ID" 2>/dev/null || true
    done_ "Admin consent applied"

    # Graph API needs time to propagate permissions.
    # First grant: 60s. Subsequent runs: 10s.
    if $NEW_PERMS_ADDED; then
        wait_progress 60 "Consent propagation"
    else
        wait_progress 10 "Consent propagation"
    fi

    # ── Step 3: Create temporary client secret ───────────────────────────────
    step "Creating temporary client secret (expires in 1 hour)..."

    END_DATE=$(date -u -d "+1 hour" '+%Y-%m-%dT%H:%M:%SZ' 2>/dev/null || date -u -v+1H '+%Y-%m-%dT%H:%M:%SZ')
    APP_OBJECT_ID=$(az ad app show --id "$SP_CLIENT_ID" --query id -o tsv 2>/dev/null || true)
    CRED_NAME="terraform-deploy-$(date '+%Y%m%d-%H%M%S')"
    CRED_BODY_FILE=$(mktemp)
    cat > "$CRED_BODY_FILE" <<ENDJSON
{"passwordCredential":{"displayName":"$CRED_NAME","endDateTime":"$END_DATE"}}
ENDJSON
    CRED_RESULT=$(az rest --method POST \
        --url "https://graph.microsoft.com/v1.0/applications/$APP_OBJECT_ID/addPassword" \
        --body "@$CRED_BODY_FILE" -o json 2>/dev/null || true)
    rm -f "$CRED_BODY_FILE" 2>/dev/null || true
    if [[ -n "$CRED_RESULT" ]]; then
        SP_TEMP_SECRET=$(echo "$CRED_RESULT" | jq -r '.secretText')
        export TF_VAR_msgraph_client_id="$SP_CLIENT_ID"
        export TF_VAR_msgraph_client_secret="$SP_TEMP_SECRET"
        export TF_VAR_msgraph_tenant_id="$TENANT_ID"
        done_ "Temporary secret created (expires: $END_DATE)"

        echo ""
        echo -e "    ${G}┌─────────────────────────────────────────────────────┐${W}"
        echo -e "    ${G}│  Agent Identity ready                               │${W}"
        echo -e "    ${G}│                                                     │${W}"
        echo -e "    ${G}│${W}  SP:      $SP_APP_NAME"
        echo -e "    ${G}│${W}  Tenant:  $TENANT_ID"
        echo -e "    ${G}│${W}  Expires: $END_DATE"
        echo -e "    ${G}│                                                     │${W}"
        echo -e "    ${G}│${W}  Terraform's msgraph provider will use this SP to"
        echo -e "    ${G}│${W}  create Agent Identity Blueprints in Entra."
        echo -e "    ${G}│${W}  Secret is cleaned up when deploy finishes."
        echo -e "    ${G}└─────────────────────────────────────────────────────┘${W}"
        echo ""
    else
        echo -e "    ${R}✗ Could not create client secret${W}"
        echo -e "      ${R}Agent Identity blueprints will not be created.${W}"
        echo -e "      ${D}Other resources will deploy normally.${W}"
    fi
fi

if [[ ${#tfvars_files[@]} -eq 1 ]]; then
    ENVIRONMENT="${tfvars_files[0]%.tfvars}"
    echo -e "    Found environment: ${C}${ENVIRONMENT}${W}"
else
    echo -e "    ${D}Available environments:${W}"
    echo ""
    for (( i=0; i<${#tfvars_files[@]}; i++ )); do
        env_name="${tfvars_files[$i]%.tfvars}"
        echo -e "      ${C}$((i+1)). ${env_name}${W}"
    done
    echo ""
    read -p "    Select environment [1-${#tfvars_files[@]}]: " pick
    if [[ "$pick" =~ ^[0-9]+$ ]]; then
        idx=$((pick - 1))
        if (( idx >= 0 && idx < ${#tfvars_files[@]} )); then
            ENVIRONMENT="${tfvars_files[$idx]%.tfvars}"
        else
            echo "Invalid selection: $pick"; exit 1
        fi
    else
        echo "Invalid selection: $pick"; exit 1
    fi
fi

# ── Read config ──────────────────────────────────────────────────────────────────

TFVARS_FILE="$TERRAFORM_DIR/${ENVIRONMENT}.tfvars"
BACKEND_FILE="$TERRAFORM_DIR/backend.hcl"

[[ -f "$BACKEND_FILE" ]] || { echo "backend.hcl not found — run init.sh first."; exit 1; }

RESOURCE_GROUP=$(read_hcl_value "$TFVARS_FILE" "resource_group_name")
LOCATION=$(read_hcl_value "$TFVARS_FILE" "location")
STORAGE_ACCOUNT=$(read_hcl_value "$BACKEND_FILE" "storage_account_name")

if [[ -z "$RESOURCE_GROUP" || -z "$STORAGE_ACCOUNT" || -z "$ENVIRONMENT" ]]; then
    echo "Could not read required values from config files. Re-run init.sh."; exit 1
fi

banner

echo -e "    Environment:     ${C}${ENVIRONMENT}${W}"
echo -e "    Resource group:  ${C}${RESOURCE_GROUP}${W}"
echo -e "    Storage account: ${C}${STORAGE_ACCOUNT}${W}"
echo -e "    Location:        ${C}${LOCATION}${W}"
echo ""

# ── Get deployer IP ──────────────────────────────────────────────────────────
DEPLOYER_IP=$(curl -s --max-time 10 https://api.ipify.org)
echo -e "    Deployer IP:     ${C}${DEPLOYER_IP}${W}"
echo ""

# ── Add deployer IP to all resource firewalls (for plan/apply state refresh) ──
add_deployer_firewall_rules() {
    local RG="$1" IP="$2"
    echo ""
    echo -e "  ${Y}━━ Adding deployer IP $IP to resource firewalls ━━${W}"

    # Key Vault
    echo -ne "    Key Vaults..."
    for kv in $(az keyvault list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      az keyvault network-rule add --name "$kv" --ip-address "$IP/32" 2>/dev/null || true
    done
    echo " done"

    # Storage accounts
    echo -ne "    Storage accounts..."
    for st in $(az storage account list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      az storage account network-rule add --resource-group "$RG" --account-name "$st" --ip-address "$IP" 2>/dev/null || true
    done
    echo " done"

    # Cosmos DB (--no-wait: the ARM operation is slow but the firewall rule takes effect immediately)
    echo -ne "    Cosmos DB..."
    for c in $(az cosmosdb list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      EXISTING=$(az cosmosdb show --name "$c" --resource-group "$RG" --query "ipRules[].ipAddressOrRange" -o tsv 2>/dev/null || true)
      if ! echo "$EXISTING" | grep -qF "$IP"; then
          NEW_IPS=$(echo -e "${EXISTING}\n${IP}" | grep -v '^$' | paste -sd, -)
          az cosmosdb update --name "$c" --resource-group "$RG" --ip-range-filter "$NEW_IPS" --no-wait 2>/dev/null || true
      fi
    done
    echo " done"

    # Cognitive Services
    echo -ne "    Cognitive Services..."
    for cog in $(az cognitiveservices account list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      az cognitiveservices account network-rule add --resource-group "$RG" --name "$cog" --ip-address "$IP/32" 2>/dev/null || true
    done
    echo " done"

    # AI Search
    echo -ne "    AI Search..."
    for s in $(az search service list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      CURRENT=$(az search service show --name "$s" --resource-group "$RG" --query "networkRuleSet.ipRules[].value" -o tsv 2>/dev/null || true)
      if ! echo "$CURRENT" | grep -qF "$IP"; then
          ALL_IPS=$(echo -e "${CURRENT}\n${IP}" | grep -v '^$')
          RULES=$(echo "$ALL_IPS" | jq -Rn '[inputs | {value: .}]')
          az search service update --name "$s" --resource-group "$RG" --ip-rules "$RULES" 2>/dev/null || true
      fi
    done
    echo " done"

    echo -e "  ${G}━━ Deployer IP added to all resource firewalls ━━${W}"
}

# ── Cleanup: ALWAYS remove deployer IP from all firewalls (even on error) ────
cleanup_deployer_ip() {
    echo ""
    echo -e "  ${Y}━━ Removing deployer IP $DEPLOYER_IP from all firewalls (always runs) ━━${W}"

    RG="$RESOURCE_GROUP"
    IP="$DEPLOYER_IP"

    # Key Vault
    echo -ne "    Key Vaults..."
    for kv in $(az keyvault list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      az keyvault network-rule remove --name "$kv" --ip-address "$IP/32" 2>/dev/null || true
    done
    echo " done"

    # Storage accounts
    echo -ne "    Storage accounts..."
    for st in $(az storage account list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      az storage account network-rule remove --resource-group "$RG" --account-name "$st" --ip-address "$IP" 2>/dev/null || true
    done
    echo " done"

    # Cosmos DB (--no-wait: the ARM operation is slow but the firewall rule takes effect immediately)
    echo -ne "    Cosmos DB..."
    for c in $(az cosmosdb list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      EXISTING=$(az cosmosdb show --name "$c" --resource-group "$RG" --query "ipRules[].ipAddressOrRange" -o tsv 2>/dev/null || true)
      FILTERED=$(echo "$EXISTING" | grep -v "^${IP}$" | paste -sd, - 2>/dev/null || true)
      az cosmosdb update --name "$c" --resource-group "$RG" --ip-range-filter "$FILTERED" --no-wait 2>/dev/null || true
    done
    echo " done"

    # Cognitive Services
    echo -ne "    Cognitive Services..."
    for cog in $(az cognitiveservices account list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      az cognitiveservices account network-rule remove --resource-group "$RG" --name "$cog" --ip-address "$IP/32" 2>/dev/null || true
    done
    echo " done"

    # AI Search
    echo -ne "    AI Search..."
    for s in $(az search service list --resource-group "$RG" --query "[].name" -o tsv 2>/dev/null); do
      CURRENT=$(az search service show --name "$s" --resource-group "$RG" --query "networkRuleSet.ipRules[].value" -o tsv 2>/dev/null || true)
      FILTERED=$(echo "$CURRENT" | grep -v "^${IP}$" || true)
      if [ -n "$FILTERED" ]; then
        RULES=$(echo "$FILTERED" | jq -Rn '[inputs | {value: .}]')
      else
        RULES="[]"
      fi
      az search service update --name "$s" --resource-group "$RG" --ip-rules "$RULES" 2>/dev/null || true
    done
    echo " done"

    echo -e "  ${G}━━ Cleanup complete ━━${W}"

    # Remove the temporary client secret created for the msgraph provider
    if [[ -n "$SP_CLIENT_ID" && -n "$SP_TEMP_SECRET" ]]; then
        echo -e "  ${D}Cleaning up temporary client secret...${W}"
        # List and remove all credentials with our naming pattern
        for KEY_ID in $(az ad app credential list --id "$SP_CLIENT_ID" \
            --query "[?starts_with(displayName, 'terraform-deploy-')].keyId" -o tsv 2>/dev/null); do
            az ad app credential delete --id "$SP_CLIENT_ID" --key-id "$KEY_ID" 2>/dev/null || true
        done
        unset TF_VAR_msgraph_client_secret
        echo -e "  ${D}✓ Temporary secrets removed${W}"
    fi
}
trap cleanup_deployer_ip EXIT

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 1 — Open resource firewalls
# ═══════════════════════════════════════════════════════════════════════════════

phase 1 "Open resource firewalls"
info_ "All Azure resources have network firewalls (Deny by default)."
info_ "Your IP must be allowlisted so Terraform can reach them."
info_ "Firewalls are removed again at the end (see cleanup)."

step "Adding deployer IP to all resource firewalls"

add_deployer_firewall_rules "$RESOURCE_GROUP" "$DEPLOYER_IP"

wait_progress 30 "Firewall propagation"
done_ "All firewalls open"

phase_summary 1 \
    "Phase 2 — terraform init (configure backend)" \
    "Deployer IP" "$DEPLOYER_IP" \
    "Status"      "All firewalls open"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 2 — terraform init
# ═══════════════════════════════════════════════════════════════════════════════

phase 2 "terraform init"
info_ "Connects Terraform to the remote state backend (Azure Storage)."
info_ "Also imports existing Entra test users to prevent conflicts."

step "Initializing Terraform with backend config"

pushd "$TERRAFORM_DIR" >/dev/null
terraform init -upgrade -reconfigure -backend-config=backend.hcl
done_ "Terraform initialized"
popd >/dev/null

phase_summary 2 \
    "Phase 3 — terraform validate (check configuration syntax)" \
    "Backend" "azurerm ($STORAGE_ACCOUNT/tfstate/$ENVIRONMENT.tfstate)" \
    "Status"  "Initialized"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 3 — terraform validate
# ═══════════════════════════════════════════════════════════════════════════════

phase 3 "terraform validate"
info_ "Checks all .tf files for syntax errors before planning."

step "Validating Terraform configuration"

pushd "$TERRAFORM_DIR" >/dev/null
terraform validate
done_ "Configuration is valid"
popd >/dev/null

phase_summary 3 \
    "Phase 4 — terraform plan (preview infrastructure changes)" \
    "Status" "Valid"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 4 — terraform plan
# ═══════════════════════════════════════════════════════════════════════════════

phase 4 "terraform plan"
info_ "Previews what resources will be created, changed, or destroyed."
info_ "No changes are applied yet \u2014 review the plan before continuing."

step "Planning infrastructure changes"

pushd "$TERRAFORM_DIR" >/dev/null
terraform plan -var-file="${ENVIRONMENT}.tfvars" -out="tfplan"
done_ "Plan saved to tfplan"
popd >/dev/null

phase_summary 4 \
    "Phase 5 — terraform apply (provision all resources)" \
    "Plan file" "tfplan" \
    "Status"    "Ready to apply"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 5 — terraform apply
# ═══════════════════════════════════════════════════════════════════════════════

phase 5 "terraform apply"
info_ "Provisions all Azure resources: AI Foundry, Cosmos DB, AKS, ACR,"
info_ "Key Vault, Storage, AI Search, VNet, Private Endpoints, RBAC."
info_ "Also uploads product images and SharePoint PDFs to blob storage."

step "Applying infrastructure changes"
echo -e "    ${D}Resources: AI Foundry, Cosmos DB, AI Search, AKS, ACR, Key Vault, Storage${W}"
echo ""

pushd "$TERRAFORM_DIR" >/dev/null
if ! terraform apply "tfplan"; then
    # ── Post-failure diagnostic: list deny policies that may have caused the failure ──
    echo ""
    echo -e "    ${Y}━━ Azure Policy diagnostic ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${W}"
    echo -e "    ${Y}If you see RequestDisallowedByPolicy above, a deny policy${W}"
    echo -e "    ${Y}blocked resource creation. Listing deny policies...${W}"
    echo ""

    SUBSCRIPTION_ID=$(az account show --query id -o tsv)
    RG_SCOPE="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP"
    SEEN_IDS=""

    for SCOPE_ARG in "--scope $RG_SCOPE" ""; do
        while IFS=$'\t' read -r ASSIGN_ID POLICY_NAME POLICY_DEF_ID ENFORCEMENT ASSIGN_PARAMS; do
            [[ -z "$ASSIGN_ID" ]] && continue
            [[ "$ENFORCEMENT" != "Default" ]] && continue
            echo "$SEEN_IDS" | grep -qF "$ASSIGN_ID" && continue
            SEEN_IDS="$SEEN_IDS $ASSIGN_ID"

            DEF_NAME=$(echo "$POLICY_DEF_ID" | awk -F'/' '{print $NF}')
            IS_INITIATIVE=$(echo "$POLICY_DEF_ID" | grep -c 'policySetDefinitions' || true)
            # Check assignment-level effect parameter override
            ASSIGN_EFFECT_OVERRIDE=$(az policy assignment show --name "$(echo "$ASSIGN_ID" | awk -F'/' '{print $NF}')" \
                --query "parameters.effect.value" -o tsv 2>/dev/null || true)

            if [[ $IS_INITIATIVE -gt 0 ]]; then
                MEMBER_DEFS=$(az policy set-definition show --name "$DEF_NAME" \
                    --query "policyDefinitions[].policyDefinitionId" -o tsv 2>/dev/null || true)
                while IFS= read -r MEMBER_ID; do
                    [[ -z "$MEMBER_ID" ]] && continue
                    M_NAME=$(echo "$MEMBER_ID" | awk -F'/' '{print $NF}')
                    # Get raw effect + resolve parameterized effects via default value
                    M_EFFECT=$(az policy definition show --name "$M_NAME" \
                        --query "policyRule.then.effect" -o tsv 2>/dev/null || true)
                    if [[ "$M_EFFECT" == *"parameters"* ]]; then
                        # Resolve: assignment override > definition default
                        if [[ -n "$ASSIGN_EFFECT_OVERRIDE" ]]; then
                            M_EFFECT="$ASSIGN_EFFECT_OVERRIDE"
                        else
                            M_DEFAULT=$(az policy definition show --name "$M_NAME" \
                                --query "parameters.effect.defaultValue" -o tsv 2>/dev/null || true)
                            [[ -n "$M_DEFAULT" ]] && M_EFFECT="$M_DEFAULT"
                        fi
                    fi
                    if [[ "${M_EFFECT,,}" == *"deny"* ]]; then
                        M_DISPLAY=$(az policy definition show --name "$M_NAME" \
                            --query "displayName" -o tsv 2>/dev/null || true)
                        echo -e "    ${R}⚠ DENY: $M_DISPLAY${W}"
                        echo -e "      ${D}Initiative: $POLICY_NAME${W}"
                    fi
                done <<< "$MEMBER_DEFS"
            else
                EFFECT=$(az policy definition show --name "$DEF_NAME" \
                    --query "policyRule.then.effect" -o tsv 2>/dev/null || true)
                if [[ "$EFFECT" == *"parameters"* ]]; then
                    if [[ -n "$ASSIGN_EFFECT_OVERRIDE" ]]; then
                        EFFECT="$ASSIGN_EFFECT_OVERRIDE"
                    else
                        DEFAULT_EFFECT=$(az policy definition show --name "$DEF_NAME" \
                            --query "parameters.effect.defaultValue" -o tsv 2>/dev/null || true)
                        [[ -n "$DEFAULT_EFFECT" ]] && EFFECT="$DEFAULT_EFFECT"
                    fi
                fi
                if [[ "${EFFECT,,}" == *"deny"* ]]; then
                    echo -e "    ${R}⚠ DENY: $POLICY_NAME${W}"
                fi
            fi
        done < <(az policy assignment list $SCOPE_ARG \
            --query "[].[id, displayName, policyDefinitionId, enforcementMode]" -o tsv 2>/dev/null || true)
    done

    echo ""
    echo -e "    ${Y}Fix your Terraform config to comply, then re-run deploy.${W}"
    echo -e "    ${Y}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${W}"
    popd >/dev/null
    exit 1
fi
done_ "All resources deployed"
popd >/dev/null

# ── Clean up ─────────────────────────────────────────────────────────────────
rm -f "$TERRAFORM_DIR/tfplan"

phase_summary 5 \
    "Phase 6 — Seed CRM data into Cosmos DB" \
    "Status" "Applied successfully"

# Wait for Cosmos DB RBAC to propagate (new role assignments need time)
# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 6 — Seed CRM data
# ═══════════════════════════════════════════════════════════════════════════════

phase 6 "Seed CRM data"

step "Resolving infrastructure endpoints from Key Vault"
KV_NAME=$(az keyvault list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv)
if [[ -z "$KV_NAME" ]]; then echo "No Key Vault found in $RESOURCE_GROUP"; exit 1; fi
COSMOS_ENDPOINT=$(az keyvault secret show --vault-name "$KV_NAME" --name "COSMOSDB-CRM-ENDPOINT" --query value -o tsv)
COSMOS_DB=$(az keyvault secret show --vault-name "$KV_NAME" --name "COSMOSDB-CRM-DATABASE" --query value -o tsv)
COSMOS_ACCOUNT_NAME=$(echo "$COSMOS_ENDPOINT" | sed 's|https://||;s|\.documents\.azure\.com.*||')
done_ "Cosmos DB: $COSMOS_ENDPOINT ($COSMOS_DB)"

# Verify deployer has Cosmos DB RBAC before attempting seed
step "Verifying Cosmos DB RBAC access..."
MAX_RETRIES=12
for (( attempt=1; attempt<=MAX_RETRIES; attempt++ )); do
    if az cosmosdb sql database show --account-name "$COSMOS_ACCOUNT_NAME" \
        --resource-group "$RESOURCE_GROUP" --name "$COSMOS_DB" -o tsv >/dev/null 2>&1; then
        done_ "RBAC verified (attempt $attempt)"
        break
    fi
    if (( attempt == MAX_RETRIES )); then
        echo -e "    ${R}Cosmos DB RBAC not active after $((MAX_RETRIES * 5))s${W}"
    else
        printf "\r    Waiting for RBAC... (attempt %d/%d)" "$attempt" "$MAX_RETRIES"
        sleep 5
    fi
done

step "Running seed-data (dotnet run -- uses DefaultAzureCredential)"
SEED_DATA_DIR="$(dirname "$SCRIPT_DIR")/src/seed-data"
pushd "$SEED_DATA_DIR" >/dev/null
COSMOSDB_CRM_ENDPOINT="$COSMOS_ENDPOINT" COSMOSDB_CRM_DATABASE="$COSMOS_DB" AZURE_TENANT_ID="$TENANT_ID" dotnet run
done_ "CRM data seeded"
popd >/dev/null

phase_summary 6 \
    "Phase 7 -- Link Entra users to Customers" \
    "Status" "CRM data seeded"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 7 — Link Entra user object IDs to Customers
# ═══════════════════════════════════════════════════════════════════════════════

phase 7 "Link Entra users to Customers"
info_ ""
info_ "Customers are JSON documents in Cosmos DB (NoSQL) with an"
info_ "entra_id field used for data scoping. Test users (Emma,"
info_ "James, etc.) are Entra ID accounts created by Terraform."
info_ ""
info_ "This phase reads each user's Entra object ID from Key Vault"
info_ "and updates the entra_id field on their Customer record."
info_ "This is how the app maps 'who logged in' to 'whose data to"
info_ "show' -- the BFF extracts the oid claim from the JWT and"
info_ "filters: WHERE c.entra_id = '<oid>'"
info_ ""
info_ "  Emma Wilson (Entra OID) --> Customer 101"
info_ "  JWT oid claim           --> entra_id filter"
info_ ""

step "Reading Entra object IDs from Key Vault"

declare -A CUSTOMER_MAPPING=(
    ["101"]="CUSTOMER-EMMA-ENTRA-OID"
    ["102"]="CUSTOMER-JAMES-ENTRA-OID"
    ["103"]="CUSTOMER-SARAH-ENTRA-OID"
    ["104"]="CUSTOMER-DAVID-ENTRA-OID"
    ["105"]="CUSTOMER-LISA-ENTRA-OID"
)

ENTRA_PAIRS=""
for CID in "${!CUSTOMER_MAPPING[@]}"; do
    SECRET_NAME="${CUSTOMER_MAPPING[$CID]}"
    OID=$(az keyvault secret show --vault-name "$KV_NAME" --name "$SECRET_NAME" --query value -o tsv 2>/dev/null || true)
    if [[ -n "$OID" ]]; then
        if [[ -n "$ENTRA_PAIRS" ]]; then ENTRA_PAIRS="${ENTRA_PAIRS};"; fi
        ENTRA_PAIRS="${ENTRA_PAIRS}${CID}=${OID}"
        done_ "Customer $CID linked"
    else
        echo -e "    ${Y}Could not read $SECRET_NAME from Key Vault${W}"
    fi
done

step "Linking Entra users to Cosmos DB Customers (dotnet run)"
pushd "$SEED_DATA_DIR" >/dev/null
COSMOSDB_CRM_ENDPOINT="$COSMOS_ENDPOINT" COSMOSDB_CRM_DATABASE="$COSMOS_DB" AZURE_TENANT_ID="$TENANT_ID" \
    CRM_DATA_PATH="/dev/null" ENTRA_MAPPING="$ENTRA_PAIRS" dotnet run
done_ "Entra users linked to Customers"
popd >/dev/null

# ── Read Key Vault URI ───────────────────────────────────────────────────────
KEYVAULT_URI=$(az keyvault show --name "$KV_NAME" --query properties.vaultUri -o tsv 2>/dev/null || true)

# ── Final summary ────────────────────────────────────────────────────────────
echo ""
echo -e "  ${G}+================================================================+${W}"
echo -e "  ${G}|                                                                |${W}"
echo -e "  ${G}|    ____  _  _  ___  ___  ____  ____  ____  _                   |${W}"
echo -e "  ${G}|   / ___)/ )( \\/ __)/ __)( ___)/ ___)/ ___)/ )                  |${W}"
echo -e "  ${G}|   \\___ \\) \\/ ( (__ ( (__  ) _) \\___ \\\\___ \\\\_/                  |${W}"
echo -e "  ${G}|   (____/\\____/\\___)\\___)(____)(_____|(_____(_)                  |${W}"
echo -e "  ${G}|                                                                |${W}"
echo -e "  ${G}|    All phases completed successfully!                          |${W}"
echo -e "  ${G}|                                                                |${W}"
echo -e "  ${G}+----------------------------------------------------------------+${W}"
printf "  ${G}|${W}  Environment:    %-48s${G}|${W}\n" "$ENVIRONMENT"
printf "  ${G}|${W}  Resource group: %-48s${G}|${W}\n" "$RESOURCE_GROUP"
printf "  ${G}|${W}  Location:       %-48s${G}|${W}\n" "$LOCATION"
if [[ -n "$KEYVAULT_URI" ]]; then
    printf "  ${G}|${W}  Key Vault URI:  %-48s${G}|${W}\n" "$KEYVAULT_URI"
fi
echo -e "  ${G}|                                                                |${W}"
echo -e "  ${G}|  Next steps (see Lab 1, Steps 2-3):                            |${W}"
echo -e "  ${G}|                                                                |${W}"
echo -e "  ${G}|    Step 2: Config Sync (open KV firewall, run, close)          |${W}"
echo -e "  ${G}|      cd src/config-sync                                        |${W}"
echo -e "  ${G}|      dotnet run -- <keyvault-uri>                              |${W}"
echo -e "  ${G}|                                                                |${W}"
echo -e "  ${G}|    Step 3: Validate (open AI Services firewall, run, close)    |${W}"
echo -e "  ${G}|      cd src/simple-agent                                       |${W}"
echo -e "  ${G}|      dotnet run                                                |${W}"
echo -e "  ${G}|                                                                |${W}"
echo -e "  ${G}+================================================================+${W}"
echo ""
