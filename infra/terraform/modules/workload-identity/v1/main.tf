# =============================================================================
# Workload Identity Federation Module v1
# Creates: Federated identity credentials binding AKS OIDC → managed identities
# =============================================================================

resource "azurerm_federated_identity_credential" "this" {
  for_each = var.federations

  name      = each.key
  parent_id = each.value.identity_id
  audience  = ["api://AzureADTokenExchange"]
  issuer    = var.aks_oidc_issuer_url
  subject   = "system:serviceaccount:${each.value.namespace}:${each.value.service_account}"
}
