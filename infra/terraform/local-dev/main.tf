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

# -----------------------------------------------------------------------------
# Microsoft Entra ID — SPA app registration + test users for MSAL sign-in
#
# Both tracks (Local + Full Azure) use real Microsoft Entra ID. The Local Track
# creates a per-developer SPA app registration with localhost callbacks plus a
# small set of test users matching the seeded customers. The same flow is used
# in the Full Azure Track via infra/terraform/main.tf.
#
# Requires the deployer to have:
#   - Application Developer (or higher) — to create the app registration
#   - User Administrator (or higher)    — to create test users
# Most M365 dev tenants grant both by default; an enterprise tenant typically
# does not. See docs/lab-0.md for tenant choices.
# -----------------------------------------------------------------------------

module "entra" {
  source = "../modules/entra/v1"

  base_name   = "${var.base_name}-local"
  environment = var.environment

  redirect_uris = [
    "http://localhost:5008/authentication/login-callback",
    "http://localhost:5008/authentication/logout-callback",
  ]

  # Default test_users (8 customers: Emma, James, Sarah, David, Lisa,
  # Mike, Anna, Tom) seeded by the entra module — same set used by the
  # Full Azure Track. Override here only if you need a different mix.
}
