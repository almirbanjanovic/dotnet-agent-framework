# =============================================================================
# Agent Identity Module v1
# Creates: Agent Identity Blueprints (app registrations) + Agent Identity
# service principals + Federated Identity Credentials for AKS workload identity.
#
# Uses the Entra Agent ID platform model:
#   Blueprint (azuread_application)
#     └── Agent Identity (azuread_service_principal)
#           └── FIC (azuread_application_federated_identity_credential) → AKS
#
# The azuread provider handles the core resources. Agent-specific metadata
# (agent subtype, blueprint-to-instance parent relationship) will be set via
# Microsoft Graph API when the Entra Agent ID platform reaches GA and provider
# support is added. For now, tags identify these as agent identities.
# =============================================================================

data "azuread_client_config" "current" {}

# -----------------------------------------------------------------------------
# Agent Identity Blueprints (Application Registrations)
# Each blueprint defines a "kind" of agent (CRM, Product, Orchestrator).
# Display name has no environment suffix — a blueprint is a type, not a deployment.
# -----------------------------------------------------------------------------

resource "azuread_application" "blueprint" {
  for_each = var.agents

  display_name     = each.value.blueprint_display_name
  owners           = [data.azuread_client_config.current.object_id]
  sign_in_audience = "AzureADMyOrg"

  api {
    requested_access_token_version = 2
  }

  tags = ["AgentIdentityBlueprint", "Contoso"]
}

# -----------------------------------------------------------------------------
# Agent Identity Service Principals
# Each service principal is the runtime identity for an agent instance.
# Display name includes the environment to identify the deployment.
# RBAC roles are assigned to the service principal's object_id.
# K8s service accounts reference the application's client_id.
# -----------------------------------------------------------------------------

resource "azuread_service_principal" "agent" {
  for_each = var.agents

  client_id = azuread_application.blueprint[each.key].client_id
  owners    = [data.azuread_client_config.current.object_id]

  tags = ["AgentIdentity", "Contoso", var.environment]
}

# -----------------------------------------------------------------------------
# Federated Identity Credentials (AKS Workload Identity)
# Binds each agent identity to a specific K8s service account in a specific
# namespace on a specific AKS cluster. Same mechanism as managed identity FIC.
# DefaultAzureCredential() in the pod resolves to this identity at runtime.
# -----------------------------------------------------------------------------

resource "azuread_application_federated_identity_credential" "aks" {
  for_each = var.agents

  application_id = azuread_application.blueprint[each.key].id
  display_name   = "aks-${replace(each.key, "_", "-")}"
  audiences      = ["api://AzureADTokenExchange"]
  issuer         = var.aks_oidc_issuer_url
  subject        = "system:serviceaccount:${each.value.namespace}:${each.value.service_account}"
}
