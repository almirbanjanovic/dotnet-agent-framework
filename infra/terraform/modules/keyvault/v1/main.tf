# =============================================================================
# Key Vault Module v1
# Creates: Azure Key Vault for storing application secrets
# =============================================================================

locals {
  keyvault_name = "kv-${var.environment}-${var.iteration}"
}

resource "azurerm_key_vault" "this" {
  name                = local.keyvault_name
  location            = var.location
  resource_group_name = var.resource_group_name
  tenant_id           = var.tenant_id
  sku_name            = "standard"

  # Use Azure RBAC for access control (no access policies)
  enable_rbac_authorization = true

  soft_delete_retention_days = var.soft_delete_retention_days
  purge_protection_enabled   = var.purge_protection_enabled

  public_network_access_enabled = true

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}
