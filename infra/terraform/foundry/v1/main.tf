# =============================================================================
# Azure OpenAI Module v1
# Creates: AI Services account + model deployment
# =============================================================================

locals {
  account_name = "aif-${var.environment}-${var.location}"
  subdomain    = lower("${local.account_name}-${replace(var.deployment_model_name, ".", "-")}")
}

# -----------------------------------------------------------------------------
# AI Services Account
# -----------------------------------------------------------------------------
resource "azurerm_cognitive_account" "this" {
  name                  = local.account_name
  location              = var.location
  resource_group_name   = var.resource_group_name
  kind                  = var.account_kind
  local_auth_enabled    = true
  sku_name              = var.sku_name
  custom_subdomain_name = local.subdomain
  tags                  = var.tags
}

# -----------------------------------------------------------------------------
# Model Deployment
# -----------------------------------------------------------------------------
resource "azurerm_cognitive_deployment" "this" {
  name                 = var.deployment_model_name
  cognitive_account_id = azurerm_cognitive_account.this.id

  model {
    format  = var.deployment_model_format
    name    = var.deployment_model_name
    version = var.deployment_model_version
  }

  sku {
    name = var.deployment_sku_name
  }

  version_upgrade_option = var.version_upgrade_option
}
