# =============================================================================
# Entra Module v1
# Creates: App registration (SPA), app roles, service principal
# =============================================================================

data "azuread_client_config" "current" {}

data "azuread_domains" "default" {
  only_default = true
}

locals {
  domain = data.azuread_domains.default.domains[0].domain_name
}

# -----------------------------------------------------------------------------
# App Registration (SPA — public client, PKCE auth, no client secret)
# -----------------------------------------------------------------------------

resource "azuread_application" "bff" {
  display_name = "app-${var.base_name}-bff-${var.environment}"
  owners       = [data.azuread_client_config.current.object_id]

  sign_in_audience = "AzureADMyOrg"

  single_page_application {
    redirect_uris = var.redirect_uris
  }

  api {
    requested_access_token_version = 2
  }

  app_role {
    allowed_member_types = ["User"]
    display_name         = "Customer"
    description          = "Can view own data, chat with agents, create support tickets"
    value                = "Customer"
    id                   = random_uuid.role_customer.result
    enabled              = true
  }
}

resource "random_uuid" "role_customer" {}

# -----------------------------------------------------------------------------
# Service Principal (Enterprise App)
# -----------------------------------------------------------------------------

resource "azuread_service_principal" "bff" {
  client_id = azuread_application.bff.client_id
  owners    = [data.azuread_client_config.current.object_id]
}
