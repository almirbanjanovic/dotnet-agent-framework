#--------------------------------------------------------------------------------------------------------------------------------
# General Configuration
#--------------------------------------------------------------------------------------------------------------------------------
locals {
  cognitive_account_kind = "OpenAI"
  oai_name               = "oai-${var.base_name}-${var.environment}-${var.location}"
  oai_sku_name           = "S0"
  oai_deployment_name    = "oai_deployment-${var.base_name}-${var.environment}-${var.location}"
  oai_deployment_sku_name = "GlobalStandard"
  oai_deployment_model_name = "gpt-4.1"
  oai_deployment_model_version = "2025-04-14"
}

#--------------------------------------------------------------------------------------------------------------------------------
# Open AI
#--------------------------------------------------------------------------------------------------------------------------------

resource "azurerm_cognitive_account" "this" {
  name                  = local.oai_name
  location              = var.location
  resource_group_name   = var.resource_group_name
  kind                  = local.cognitive_account_kind
  sku_name              = "S0"
  custom_subdomain_name = local.oai_name
  tags                  = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Model Deployments
#--------------------------------------------------------------------------------------------------------------------------------

resource "azurerm_cognitive_deployment" "this" {
  name                 = local.oai_deployment_name
  cognitive_account_id = azurerm_cognitive_account.this.id

  model {
    format  = local.cognitive_account_kind
    name    = local.oai_deployment_model_name
    version = local.oai_deployment_model_version
  }

  sku {
    name     = local.oai_deployment_sku_name
  }
}