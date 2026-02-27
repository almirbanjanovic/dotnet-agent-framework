#--------------------------------------------------------------------------------------------------------------------------------
# Open AI
#--------------------------------------------------------------------------------------------------------------------------------

resource "azurerm_cognitive_account" "this" {
  name                  = var.oai_name
  location              = var.location
  resource_group_name   = var.resource_group_name
  kind                  = var.cognitive_account_kind
  sku_name              = var.oai_sku_name
  custom_subdomain_name = var.oai_name
  tags                  = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Model Deployments
#--------------------------------------------------------------------------------------------------------------------------------

resource "azurerm_cognitive_deployment" "this" {
  name                 = var.oai_deployment_name
  cognitive_account_id = azurerm_cognitive_account.this.id

  model {
    format  = var.cognitive_account_kind
    name    = var.oai_deployment_model_name
    version = var.oai_deployment_model_version
  }

  sku {
    name     = var.oai_deployment_sku_name
  }
}