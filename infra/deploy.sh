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
    echo ""
    echo -e "    ${G}┌ Phase $num complete ─────────────────────────────────┐${W}"
    while [[ $# -gt 0 ]]; do
        echo -e "    ${G}│${W}  $1: $2"
        shift 2
    done
    echo -e "    ${G}└─────────────────────────────────────────────────────┘${W}"
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

# ── Read config ──────────────────────────────────────────────────────────────

TFVARS_FILE="$TERRAFORM_DIR/terraform.tfvars"
BACKEND_FILE="$TERRAFORM_DIR/backend.hcl"

[[ -f "$TFVARS_FILE" ]]  || { echo "terraform.tfvars not found — run init.sh first."; exit 1; }
[[ -f "$BACKEND_FILE" ]] || { echo "backend.hcl not found — run init.sh first."; exit 1; }

RESOURCE_GROUP=$(read_hcl_value "$TFVARS_FILE" "resource_group_name")
ENVIRONMENT=$(read_hcl_value "$TFVARS_FILE" "environment")
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
terraform init -reconfigure -backend-config=backend.hcl
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
terraform plan -var-file="terraform.tfvars" -out="tfplan"
done_ "Plan saved to tfplan"
popd >/dev/null

phase_summary 4 \
    "Phase 5 — terraform apply (provision all resources + seed data)" \
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
done_ "All resources deployed and data seeded"
popd >/dev/null

# ── Clean up ─────────────────────────────────────────────────────────────────
rm -f "$TERRAFORM_DIR/tfplan"

pushd "$TERRAFORM_DIR" >/dev/null
KEYVAULT_URI=$(terraform output -raw keyvault_uri 2>/dev/null || true)
popd >/dev/null

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
