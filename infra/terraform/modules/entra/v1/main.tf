# =============================================================================
# Entra Module v1
# Creates: App registration, app roles, service principal, client secret
# =============================================================================

data "azuread_client_config" "current" {}

data "azuread_domains" "default" {
  only_default = true
}

locals {
  domain = data.azuread_domains.default.domains[0].domain_name
}

# -----------------------------------------------------------------------------
# App Registration
# -----------------------------------------------------------------------------

resource "azuread_application" "bff" {
  display_name = "app-${var.base_name}-bff-${var.environment}"
  owners       = [data.azuread_client_config.current.object_id]

  sign_in_audience = "AzureADMyOrg"

  web {
    redirect_uris = var.redirect_uris

    implicit_grant {
      id_token_issuance_enabled = true
    }
  }

  app_role {
    allowed_member_types = ["User"]
    display_name         = "Agent User"
    description          = "Can view customer data and chat with agents"
    value                = "Agent.User"
    id                   = random_uuid.role_agent_user.result
    enabled              = true
  }

  app_role {
    allowed_member_types = ["User"]
    display_name         = "Data Writer"
    description          = "Can create and update support tickets"
    value                = "Data.Writer"
    id                   = random_uuid.role_data_writer.result
    enabled              = true
  }
}

resource "random_uuid" "role_agent_user" {}
resource "random_uuid" "role_data_writer" {}

# -----------------------------------------------------------------------------
# Service Principal (Enterprise App)
# -----------------------------------------------------------------------------

resource "azuread_service_principal" "bff" {
  client_id = azuread_application.bff.client_id
  owners    = [data.azuread_client_config.current.object_id]
}

# -----------------------------------------------------------------------------
# Client Secret
# -----------------------------------------------------------------------------

resource "azuread_application_password" "bff" {
  application_id = azuread_application.bff.id
  display_name   = "bff-secret-${var.environment}"
  end_date       = timeadd(timestamp(), "8760h") # 1 year

  lifecycle {
    ignore_changes = [end_date]
  }
}
