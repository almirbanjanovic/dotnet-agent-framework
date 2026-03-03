#--------------------------------------------------------------------------------------------------------------------------------
# General Configuration
#--------------------------------------------------------------------------------------------------------------------------------
locals {
  resource_suffix     = "${var.environment}-${var.location}"
  foundry_name            = "aif-${local.resource_suffix}"
  oai_deployment_name = "oai-deployment-${local.resource_suffix}"
}

#--------------------------------------------------------------------------------------------------------------------------------
# Open AI
#--------------------------------------------------------------------------------------------------------------------------------

resource "azurerm_cognitive_account" "this" {
  name                  = local.foundry_name
  location              = var.location
  resource_group_name   = var.resource_group_name
  kind                  = var.cognitive_account_kind
  sku_name              = var.oai_sku_name
  custom_subdomain_name = local.foundry_name
  tags                  = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Model Deployments
#--------------------------------------------------------------------------------------------------------------------------------

resource "azurerm_cognitive_deployment" "this" {
  name                 = local.oai_deployment_name
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