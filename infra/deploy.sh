#!/usr/bin/env bash
set -euo pipefail

# ═══════════════════════════════════════════════════════════════════════════════
# .NET Agent Framework — Lab 1 Deploy
#
# Mirrors the CI/CD workflow: unlocks state storage, runs terraform
# init/validate/plan/apply, then shows next steps.
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
    echo -e "  ${C}╔═══════════════════════════════════════════════════════╗${W}"
    echo -e "  ${C}║                                                       ║${W}"
    echo -e "  ${C}║   .NET Agent Framework — Lab 1 Deploy                 ║${W}"
    echo -e "  ${C}║                                                       ║${W}"
    echo -e "  ${C}║   This script deploys all Azure infrastructure:       ║${W}"
    echo -e "  ${C}║     1. Unlock state storage                           ║${W}"
    echo -e "  ${C}║     2. terraform init                                 ║${W}"
    echo -e "  ${C}║     3. terraform validate                             ║${W}"
    echo -e "  ${C}║     4. terraform plan                                 ║${W}"
    echo -e "  ${C}║     5. terraform apply                                ║${W}"
    echo -e "  ${C}║     6. Seed CRM data                                  ║${W}"
    echo -e "  ${C}║     7. Link Entra users to Customers                  ║${W}"
    echo -e "  ${C}║                                                       ║${W}"
    echo -e "  ${C}╚═══════════════════════════════════════════════════════╝${W}"
    echo -e ""
}

phase() {
    echo -e ""
    echo -e "  ${D}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${W}"
    echo -e "  ${C}Phase $1 — $2${W}"
    echo -e "  ${D}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${W}"
}

step()  { echo -e "  → $1"; }
done_() { echo -e "    ${G}✓ $1${W}"; }

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
    read -p "    Continue? (Y/n) " response
    if [[ "$response" == "n" || "$response" == "N" ]]; then
        echo -e "    ${Y}Stopped by user.${W}"
        exit 0
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

# ── Azure login ───────────────────────────────────────────────────────────────────
az config set core.login_experience_v2=off 2>/dev/null
az config set core.enable_broker_on_windows=false 2>/dev/null
echo -e "    ${D}Signing in to Azure — select the correct account in the browser.${W}"
echo ""
# Request Graph scope upfront to avoid stale-token errors during Key Vault reads.
az login --scope https://graph.microsoft.com/.default >/dev/null

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

    echo -e "  ${Y}━━ Removing deployer IP from state storage ━━${W}"
    az storage account network-rule remove --account-name "$STORAGE_ACCOUNT" --resource-group "$RESOURCE_GROUP" --ip-address "$DEPLOYER_IP" >/dev/null 2>&1 || true
    echo -e "  ${G}━━ Cleanup complete ━━${W}"
}
trap cleanup_deployer_ip EXIT

# ═══════════════════════════════════════════════════════════════════════════════
# PRE-FLIGHT — Purge soft-deleted resources from previous runs
# Azure keeps deleted Key Vaults, Cognitive Services, and other resources in a
# soft-deleted state. These block re-creation with the same name. Purge them
# as a fail-safe before Terraform runs.
# ═══════════════════════════════════════════════════════════════════════════════

step "Checking for soft-deleted Cognitive Services accounts"
SOFT_COG=$(az cognitiveservices account list-deleted --query "[?contains(id, '$RESOURCE_GROUP')].[name]" -o tsv 2>/dev/null || true)
if [[ -n "$SOFT_COG" ]]; then
    while IFS= read -r acct; do
        acct=$(echo "$acct" | xargs)
        [[ -z "$acct" ]] && continue
        echo -e "    ${Y}Purging soft-deleted account: $acct${W}"
        az cognitiveservices account purge --location "$LOCATION" --resource-group "$RESOURCE_GROUP" --name "$acct" 2>/dev/null || true
        done_ "Purged $acct"
    done <<< "$SOFT_COG"
else
    done_ "No soft-deleted Cognitive Services accounts found"
fi

step "Checking for soft-deleted Key Vaults"
SOFT_KV=$(az keyvault list-deleted --query "[?properties.vaultId && contains(properties.vaultId, '$RESOURCE_GROUP')].[name]" -o tsv 2>/dev/null || true)
if [[ -n "$SOFT_KV" ]]; then
    while IFS= read -r kv; do
        kv=$(echo "$kv" | xargs)
        [[ -z "$kv" ]] && continue
        echo -e "    ${Y}Purging soft-deleted Key Vault: $kv${W}"
        az keyvault purge --name "$kv" --no-wait 2>/dev/null || true
        done_ "Purged $kv (async)"
    done <<< "$SOFT_KV"
else
    done_ "No soft-deleted Key Vaults found"
fi

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 1 — Unlock state storage
# ═══════════════════════════════════════════════════════════════════════════════

phase 1 "Unlock state storage"

step "Adding deployer IP to $STORAGE_ACCOUNT firewall"

az storage account network-rule add \
    --account-name "$STORAGE_ACCOUNT" \
    --resource-group "$RESOURCE_GROUP" \
    --ip-address "$DEPLOYER_IP" >/dev/null

echo -e "    ${D}Waiting 30s for firewall change to propagate...${W}"
sleep 30
done_ "Deployer IP added to $STORAGE_ACCOUNT"

phase_summary 1 \
    "Phase 2 — terraform init (configure backend)" \
    "Storage account" "$STORAGE_ACCOUNT" \
    "Deployer IP"     "$DEPLOYER_IP"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 2 — terraform init
# ═══════════════════════════════════════════════════════════════════════════════

phase 2 "terraform init"

step "Initializing Terraform with backend config"

pushd "$TERRAFORM_DIR" >/dev/null
terraform init -upgrade -reconfigure -backend-config=backend.hcl
done_ "Terraform initialized"

# ── Import existing Entra users into state (idempotent) ──────────────────────
step "Importing existing Entra users into Terraform state (if needed)"
DOMAIN=$(az rest --method GET --url "https://graph.microsoft.com/v1.0/domains" \
  --query "value[?isDefault].id" -o tsv 2>/dev/null || true)

declare -A USER_MAP=(
    ["emma"]="emma.wilson"
    ["james"]="james.chen"
    ["sarah"]="sarah.miller"
    ["david"]="david.park"
    ["lisa"]="lisa.torres"
)

for key in "${!USER_MAP[@]}"; do
    UPN="${USER_MAP[$key]}@${DOMAIN}"
    IN_STATE=$(terraform state list "module.entra.azuread_user.test[\"$key\"]" 2>/dev/null || true)
    if [[ -z "$IN_STATE" ]]; then
        OID=$(az ad user show --id "$UPN" --query id -o tsv 2>/dev/null || true)
        if [[ -n "$OID" ]]; then
            echo -e "    ${D}Importing $UPN → state${W}"
            terraform import "module.entra.azuread_user.test[\"$key\"]" "$OID" 2>/dev/null || true
        fi
    fi
done
done_ "Entra users synced with state"
popd >/dev/null

phase_summary 2 \
    "Phase 3 — terraform validate (check configuration syntax)" \
    "Backend" "azurerm ($STORAGE_ACCOUNT/tfstate/$ENVIRONMENT.tfstate)" \
    "Status"  "Initialized"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 3 — terraform validate
# ═══════════════════════════════════════════════════════════════════════════════

phase 3 "terraform validate"

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

# Re-add deployer IP to all resource firewalls before plan. On a retry after a
# previous failed deploy, the cleanup trap will have stripped the deployer IP from
# every firewall. Terraform plan refreshes existing resources (blobs, certs) via
# data-plane calls, so the firewalls must be open before plan runs.
step "Ensuring deployer IP is whitelisted on existing resource firewalls"
add_deployer_firewall_rules "$RESOURCE_GROUP" "$DEPLOYER_IP"
echo -e "    ${D}Waiting 30s for firewall changes to propagate...${W}"
sleep 30

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

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 6 — Seed CRM data
# ═══════════════════════════════════════════════════════════════════════════════

phase 6 "Seed CRM data"

step "Discovering Key Vault in $RESOURCE_GROUP"
KV_NAME=$(az keyvault list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv)
if [[ -z "$KV_NAME" ]]; then echo "No Key Vault found in $RESOURCE_GROUP"; exit 1; fi
done_ "Key Vault: $KV_NAME"

step "Reading Cosmos DB CRM connection info from Key Vault"
COSMOS_ENDPOINT=$(az keyvault secret show --vault-name "$KV_NAME" --name "COSMOSDB-CRM-ENDPOINT" --query value -o tsv)
COSMOS_KEY=$(az keyvault secret show --vault-name "$KV_NAME" --name "COSMOSDB-CRM-KEY" --query value -o tsv)
COSMOS_DB=$(az keyvault secret show --vault-name "$KV_NAME" --name "COSMOSDB-CRM-DATABASE" --query value -o tsv)
done_ "Cosmos DB: $COSMOS_ENDPOINT / $COSMOS_DB"

step "Getting AKS credentials (Cosmos DB is behind a private endpoint)"
AKS_NAME=$(az aks list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv)
if [[ -z "$AKS_NAME" ]]; then echo "No AKS cluster found in $RESOURCE_GROUP"; exit 1; fi
az aks get-credentials --resource-group "$RESOURCE_GROUP" --name "$AKS_NAME" --overwrite-existing >/dev/null
done_ "AKS: $AKS_NAME"

step "Publishing seed-data tool for Linux"
SEED_DATA_DIR="$(dirname "$SCRIPT_DIR")/src/seed-data"
PUBLISH_DIR="$SEED_DATA_DIR/bin/publish"
pushd "$SEED_DATA_DIR" >/dev/null
dotnet publish -c Release -r linux-x64 --self-contained -o "$PUBLISH_DIR" >/dev/null 2>&1
done_ "Published to $PUBLISH_DIR"
popd >/dev/null

step "Running seed-data inside AKS cluster"
K8S_NAMESPACE="contoso"
POD_NAME="seed-data-runner"
CRM_DATA_DIR="$(dirname "$SCRIPT_DIR")/data/contoso-crm"

kubectl run "$POD_NAME" --image=mcr.microsoft.com/dotnet/runtime-deps:9.0 --restart=Never --namespace="$K8S_NAMESPACE" --command -- sleep 600 >/dev/null 2>&1
kubectl wait --for=condition=Ready "pod/$POD_NAME" --namespace="$K8S_NAMESPACE" --timeout=120s >/dev/null 2>&1
done_ "Pod $POD_NAME ready"

cleanup_seed_pod() {
    kubectl delete pod "$POD_NAME" --namespace="$K8S_NAMESPACE" --force --grace-period=0 >/dev/null 2>&1 || true
}
# Pod cleanup handled inline below (trap EXIT reserved for deployer IP cleanup)

kubectl exec "$POD_NAME" --namespace="$K8S_NAMESPACE" -- mkdir -p /app /data/contoso-crm >/dev/null 2>&1
kubectl cp "$PUBLISH_DIR" "${K8S_NAMESPACE}/${POD_NAME}:/app" >/dev/null 2>&1
kubectl cp "$CRM_DATA_DIR" "${K8S_NAMESPACE}/${POD_NAME}:/data/contoso-crm" >/dev/null 2>&1
kubectl exec "$POD_NAME" --namespace="$K8S_NAMESPACE" -- chmod +x /app/seed-data >/dev/null 2>&1

kubectl exec "$POD_NAME" --namespace="$K8S_NAMESPACE" -- \
    /bin/sh -c "COSMOSDB_CRM_ENDPOINT='$COSMOS_ENDPOINT' COSMOSDB_CRM_KEY='$COSMOS_KEY' COSMOSDB_CRM_DATABASE='$COSMOS_DB' CRM_DATA_PATH='/data/contoso-crm' /app/seed-data"
done_ "CRM data seeded"

kubectl delete pod "$POD_NAME" --namespace="$K8S_NAMESPACE" --force --grace-period=0 >/dev/null 2>&1 || true
# (trap - EXIT removed — deployer IP cleanup trap handles all cleanup)

phase_summary 6 \
    "Phase 7 \u2014 Link Entra users to Customers" \
    "Status" "CRM data seeded"

# \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550
# PHASE 7 — Link Entra user object IDs to Customers
# \u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550\u2550

phase 7 "Link Entra users to Customers"

step "Reading Entra object IDs from Key Vault"

declare -A CUSTOMER_MAPPING=(
    ["101"]="CUSTOMER-EMMA-ENTRA-OID"
    ["102"]="CUSTOMER-JAMES-ENTRA-OID"
    ["103"]="CUSTOMER-SARAH-ENTRA-OID"
    ["104"]="CUSTOMER-DAVID-ENTRA-OID"
    ["105"]="CUSTOMER-LISA-ENTRA-OID"
)

step "Building Entra mapping and updating Cosmos DB via seed-data tool"
ENTRA_PAIRS=""
for CID in "${!CUSTOMER_MAPPING[@]}"; do
    SECRET_NAME="${CUSTOMER_MAPPING[$CID]}"
    OID=$(az keyvault secret show --vault-name "$KV_NAME" --name "$SECRET_NAME" --query value -o tsv 2>/dev/null || true)
    if [[ -n "$OID" ]]; then
        if [[ -n "$ENTRA_PAIRS" ]]; then ENTRA_PAIRS="${ENTRA_PAIRS};"; fi
        ENTRA_PAIRS="${ENTRA_PAIRS}${CID}=${OID}"
    else
        echo -e "    ${Y}Could not read $SECRET_NAME from Key Vault${W}"
    fi
done

# Reuse the seed-data pod pattern to run entra linking inside the cluster
POD_NAME7="entra-linker"
kubectl run "$POD_NAME7" --image=mcr.microsoft.com/dotnet/runtime-deps:9.0 --restart=Never --namespace="$K8S_NAMESPACE" --command -- sleep 300 >/dev/null 2>&1
kubectl wait --for=condition=Ready "pod/$POD_NAME7" --namespace="$K8S_NAMESPACE" --timeout=120s >/dev/null 2>&1

kubectl exec "$POD_NAME7" --namespace="$K8S_NAMESPACE" -- mkdir -p /app >/dev/null 2>&1
kubectl cp "$PUBLISH_DIR" "${K8S_NAMESPACE}/${POD_NAME7}:/app" >/dev/null 2>&1
kubectl exec "$POD_NAME7" --namespace="$K8S_NAMESPACE" -- chmod +x /app/seed-data >/dev/null 2>&1

kubectl exec "$POD_NAME7" --namespace="$K8S_NAMESPACE" -- \
    /bin/sh -c "COSMOSDB_CRM_ENDPOINT='$COSMOS_ENDPOINT' COSMOSDB_CRM_KEY='$COSMOS_KEY' COSMOSDB_CRM_DATABASE='$COSMOS_DB' CRM_DATA_PATH='/dev/null' ENTRA_MAPPING='$ENTRA_PAIRS' /app/seed-data"
done_ "Entra users linked to Customers"

kubectl delete pod "$POD_NAME7" --namespace="$K8S_NAMESPACE" --force --grace-period=0 >/dev/null 2>&1 || true

# ── Read Key Vault URI ───────────────────────────────────────────────────────
KEYVAULT_URI=$(az keyvault show --name "$KV_NAME" --query properties.vaultUri -o tsv 2>/dev/null || true)

# ── Final summary ────────────────────────────────────────────────────────────
echo ""
echo -e "  ${G}╔═══════════════════════════════════════════════════════╗${W}"
echo -e "  ${G}║  Deployment Complete!                                 ║${W}"
echo -e "  ${G}╠═══════════════════════════════════════════════════════╣${W}"
echo -e "  ${G}║${W}  Environment:    $ENVIRONMENT"
echo -e "  ${G}║${W}  Resource group: $RESOURCE_GROUP"
echo -e "  ${G}║${W}  Location:       $LOCATION"
if [[ -n "$KEYVAULT_URI" ]]; then
    echo -e "  ${G}║${W}  Key Vault URI:  $KEYVAULT_URI"
fi
echo -e "  ${G}║${W}"
echo -e "  ${G}║  Next steps:                                          ${W}"
echo -e "  ${G}║    1. cd src/config-sync                               ${W}"
echo -e "  ${G}║    2. dotnet run -- ${KEYVAULT_URI}${W}"
echo -e "  ${G}║    3. cd ../simple-agent && dotnet run                 ${W}"
echo -e "  ${G}╚═══════════════════════════════════════════════════════╝${W}"
echo ""
