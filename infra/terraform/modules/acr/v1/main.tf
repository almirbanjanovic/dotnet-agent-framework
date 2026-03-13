# =============================================================================
# Azure Container Registry Module v1
# Creates: ACR (or references an existing one)
# =============================================================================

locals {
  acr_name_generated = replace("acr${var.base_name}${var.environment}${var.location}", "-", "")
}

# -----------------------------------------------------------------------------
# Create new ACR
# -----------------------------------------------------------------------------
resource "azurerm_container_registry" "this" {
  count               = var.create_acr ? 1 : 0
  name                = local.acr_name_generated
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = var.sku
  admin_enabled       = var.admin_enabled

  public_network_access_enabled = true
  network_rule_bypass_option    = "AzureServices"

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# Reference existing ACR
# -----------------------------------------------------------------------------
data "azurerm_container_registry" "existing" {
  count               = var.create_acr ? 0 : 1
  name                = var.acr_name
  resource_group_name = var.existing_acr_resource_group != "" ? var.existing_acr_resource_group : var.resource_group_name
}

locals {
  acr_id       = var.create_acr ? azurerm_container_registry.this[0].id : data.azurerm_container_registry.existing[0].id
  login_server = var.create_acr ? azurerm_container_registry.this[0].login_server : data.azurerm_container_registry.existing[0].login_server
  acr_name     = var.create_acr ? azurerm_container_registry.this[0].name : data.azurerm_container_registry.existing[0].name
}

