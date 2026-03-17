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
C='\033[36m' G='\033[32m' D='\033[90m' Y='\033[33m' W='\033[0m'

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
echo -e "    ${D}Signing in to Azure — select the correct account in the browser.${W}"
echo ""
az login >/dev/null

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

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 1 — Unlock state storage
# ═══════════════════════════════════════════════════════════════════════════════

phase 1 "Unlock state storage"

step "Enabling public access on $STORAGE_ACCOUNT"

az storage account update \
    --name "$STORAGE_ACCOUNT" \
    --resource-group "$RESOURCE_GROUP" \
    --public-network-access Enabled >/dev/null

done_ "Public access enabled on $STORAGE_ACCOUNT"

phase_summary 1 \
    "Phase 2 — terraform init (configure backend)" \
    "Storage account" "$STORAGE_ACCOUNT" \
    "Public access"   "Enabled"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 2 — terraform init
# ═══════════════════════════════════════════════════════════════════════════════

phase 2 "terraform init"

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
echo -e "    ${D}Resources: AI Foundry, SQL, Cosmos DB, AI Search, AKS, ACR, Key Vault, Storage${W}"
echo ""

pushd "$TERRAFORM_DIR" >/dev/null
terraform apply "tfplan"
done_ "All resources deployed"
popd >/dev/null

# ── Clean up ─────────────────────────────────────────────────────────────────
rm -f "$TERRAFORM_DIR/tfplan"

phase_summary 5 \
    "Phase 6 — Seed CRM data into Azure SQL Database" \
    "Status" "Applied successfully"

# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 6 — Seed CRM data
# ═══════════════════════════════════════════════════════════════════════════════

phase 6 "Seed CRM data"

step "Discovering Key Vault in $RESOURCE_GROUP"
KV_NAME=$(az keyvault list --resource-group "$RESOURCE_GROUP" --query "[0].name" -o tsv)
if [[ -z "$KV_NAME" ]]; then echo "No Key Vault found in $RESOURCE_GROUP"; exit 1; fi
done_ "Key Vault: $KV_NAME"

step "Reading SQL credentials from Key Vault"
SQL_FQDN=$(az keyvault secret show --vault-name "$KV_NAME" --name "SQL-SERVER-FQDN" --query value -o tsv)
SQL_DB=$(az keyvault secret show --vault-name "$KV_NAME" --name "SQL-DATABASE-NAME" --query value -o tsv)
SQL_LOGIN=$(az keyvault secret show --vault-name "$KV_NAME" --name "SQL-ADMIN-LOGIN" --query value -o tsv)
SQL_PASS=$(az keyvault secret show --vault-name "$KV_NAME" --name "SQL-ADMIN-PASSWORD" --query value -o tsv)
done_ "SQL Server: $SQL_FQDN / $SQL_DB"

step "Running seed-data tool"

SEED_DATA_DIR="$(dirname "$SCRIPT_DIR")/src/seed-data"
pushd "$SEED_DATA_DIR" >/dev/null
export SQL_SERVER_FQDN="$SQL_FQDN"
export SQL_DATABASE_NAME="$SQL_DB"
export SQL_ADMIN_LOGIN="$SQL_LOGIN"
export SQL_ADMIN_PASSWORD="$SQL_PASS"
dotnet run
done_ "CRM data seeded"
unset SQL_SERVER_FQDN SQL_DATABASE_NAME SQL_ADMIN_LOGIN SQL_ADMIN_PASSWORD
popd >/dev/null

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
