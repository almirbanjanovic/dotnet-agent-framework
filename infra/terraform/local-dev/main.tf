data "azurerm_client_config" "current" {}

resource "azurerm_resource_group" "this" {
  name     = var.resource_group_name != null ? var.resource_group_name : "rg-${var.base_name}-${var.environment}"
  location = var.location
}

module "foundry" {
  source = "../modules/foundry/v1"

  base_name                     = var.base_name
  environment                   = var.environment
  location                      = azurerm_resource_group.this.location
  resource_group_name           = azurerm_resource_group.this.name
  account_kind                  = "AIServices"
  sku_name                      = "S0"
  deployment_sku_name           = "GlobalStandard"
  deployment_model_format       = "OpenAI"
  deployment_model_name         = var.chat_model_name
  deployment_model_version      = var.chat_model_version
  version_upgrade_option        = "NoAutoUpgrade"
  embedding_model_name          = var.embedding_model_name
  embedding_model_version       = var.embedding_model_version
  embedding_sku_name            = "GlobalStandard"
  embedding_capacity            = 120
  local_auth_enabled            = false
  public_network_access_enabled = true
  allowed_ips                   = []

  tags = {
    environment = var.environment
    purpose     = "local-development"
  }
}

# Grant the deployer (the user running setup-local) the OpenAI User role on the
# Foundry account so DefaultAzureCredential picks up their CLI token. No API
# keys are required — every call is authenticated as the signed-in user.
resource "azurerm_role_assignment" "deployer_openai_user" {
  scope                = module.foundry.account_id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = data.azurerm_client_config.current.object_id
}
