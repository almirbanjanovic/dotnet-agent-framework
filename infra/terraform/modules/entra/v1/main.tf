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

    # Exposes the `access_as_user` delegated scope. Blazor MSAL requests
    # `api://{client_id}/access_as_user`; without this scope the BFF Entra
    # app rejects token acquisition with AADSTS70011 / AADSTS65001.
    oauth2_permission_scope {
      id                         = random_uuid.scope_access_as_user.result
      type                       = "User"
      value                      = "access_as_user"
      enabled                    = true
      admin_consent_display_name = "Access Contoso BFF as user"
      admin_consent_description  = "Allow the app to call the Contoso BFF API on behalf of the signed-in user."
      user_consent_display_name  = "Access Contoso BFF"
      user_consent_description   = "Allow the app to call the Contoso BFF API on your behalf."
    }
  }

  app_role {
    allowed_member_types = ["User"]
    display_name         = "Customer"
    description          = "Can view own data, chat with agents, create support tickets"
    value                = "Customer"
    id                   = random_uuid.role_customer.result
    enabled              = true
  }

  # The companion `azuread_application_identifier_uri.bff` resource manages
  # `identifier_uris` out-of-band (because it needs `azuread_application.bff.client_id`
  # which can't self-reference). Without this ignore_changes the parent and
  # child resources will fight on every plan, producing perpetual diffs.
  # See: https://registry.terraform.io/providers/hashicorp/azuread/3.4.0/docs/resources/application_identifier_uri
  lifecycle {
    ignore_changes = [identifier_uris]
  }
}

resource "random_uuid" "role_customer" {}
resource "random_uuid" "scope_access_as_user" {}

# Sets the App ID URI ("api://{client_id}") so the access_as_user scope
# resolves to the canonical scope identifier the Blazor client requests.
# Set in a separate resource because the application's own client_id can't
# be referenced inside its defining block.
resource "azuread_application_identifier_uri" "bff" {
  application_id = azuread_application.bff.id
  identifier_uri = "api://${azuread_application.bff.client_id}"
}

# -----------------------------------------------------------------------------
# Service Principal (Enterprise App)
# -----------------------------------------------------------------------------

resource "azuread_service_principal" "bff" {
  client_id = azuread_application.bff.client_id
  owners    = [data.azuread_client_config.current.object_id]
}
