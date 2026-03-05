#--------------------------------------------------------------------------------------------------------------------------------
# Open AI
#--------------------------------------------------------------------------------------------------------------------------------

module "openai" {
  source = "./foundry/v1"

  environment              = var.environment
  location                 = var.location
  resource_group_name      = var.resource_group_name
  account_kind             = var.cognitive_account_kind
  sku_name                 = var.oai_sku_name
  deployment_sku_name      = var.oai_deployment_sku_name
  deployment_model_format  = var.oai_deployment_model_format
  deployment_model_name    = var.oai_deployment_model_name
  deployment_model_version = var.oai_deployment_model_version
  version_upgrade_option   = var.oai_version_upgrade_option
  tags                     = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Cosmos DB
#--------------------------------------------------------------------------------------------------------------------------------

module "cosmosdb" {
  source = "./cosmosdb/v1"

  project_name        = var.cosmos_project_name
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  iteration           = var.cosmos_iteration
  database_name       = var.cosmos_database_name
  tags                = var.tags
}