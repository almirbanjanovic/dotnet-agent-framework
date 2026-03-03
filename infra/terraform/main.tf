#--------------------------------------------------------------------------------------------------------------------------------
# General Configuration
#--------------------------------------------------------------------------------------------------------------------------------
locals {
  foundry_name = "aif-${var.environment}-${var.location}"
}

#--------------------------------------------------------------------------------------------------------------------------------
# Open AI
#--------------------------------------------------------------------------------------------------------------------------------

resource "azurerm_cognitive_account" "this" {
  name                  = local.foundry_name
  location              = var.location
  resource_group_name   = var.resource_group_name
  kind                  = var.cognitive_account_kind
  local_auth_enabled    = true
  sku_name              = var.oai_sku_name
  custom_subdomain_name = "${local.foundry_name}-${replace(var.oai_deployment_model_name, ".", "-")}"
  tags                  = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Model Deployments
#--------------------------------------------------------------------------------------------------------------------------------

resource "azurerm_cognitive_deployment" "this" {
  name                 = var.oai_deployment_model_name
  cognitive_account_id = azurerm_cognitive_account.this.id

  model {
    format  = var.oai_deployment_model_format
    name    = var.oai_deployment_model_name
    version = var.oai_deployment_model_version
  }

  sku {
    name = var.oai_deployment_sku_name
  }

  version_upgrade_option = var.oai_version_upgrade_option
}