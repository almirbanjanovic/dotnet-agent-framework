# =============================================================================
# Agent Identity Module v1 (Microsoft Graph beta)
# Creates Entra Agent Identity Blueprints and Blueprint Principals via the
# Microsoft Graph beta API using the msgraph provider.
#
# IMPORTANT:
# - Agent Identity instances are created at runtime by the blueprint service,
#   NOT during Terraform provisioning. This is by design per Microsoft docs.
# - Federated Identity Credentials (FICs) bind blueprints to AKS service
#   accounts for workload identity (OIDC token exchange).
#
# References:
# - https://learn.microsoft.com/en-us/entra/agent-id/identity-platform/create-blueprint
# - https://learn.microsoft.com/en-us/entra/agent-id/identity-platform/create-delete-agent-identities
#
# Prerequisite: Microsoft 365 Copilot license + Frontier program enrollment.
# =============================================================================

# -----------------------------------------------------------------------------
# Agent Identity Blueprints (Graph beta: AgentIdentityBlueprint)
# -----------------------------------------------------------------------------

resource "msgraph_resource" "blueprint" {
  for_each    = var.agents
  url         = "applications"
  api_version = "beta"

  body = {
    "@odata.type"  = "#Microsoft.Graph.AgentIdentityBlueprint"
    displayName    = each.value.blueprint_display_name
    signInAudience = "AzureADMyOrg"
  }

  # Only read back the properties we care about. Without this, Graph API
  # returns owners@odata.bind and sponsors@odata.bind on GET, which aren't
  # in our body — causing a plan diff and RBAC churn on every run.
  read_query_parameters = {
    "$select" = ["id", "appId", "displayName", "signInAudience"]
  }

  response_export_values = {
    appId       = "appId"
    id          = "id"
    displayName = "displayName"
  }
}

# -----------------------------------------------------------------------------
# Agent Identity Blueprint Principals (Graph beta: AgentIdentityBlueprintPrincipal)
# -----------------------------------------------------------------------------

resource "msgraph_resource" "blueprint_principal" {
  for_each    = var.agents
  url         = "servicePrincipals"
  api_version = "beta"

  body = {
    "@odata.type" = "#Microsoft.Graph.AgentIdentityBlueprintPrincipal"
    appId         = msgraph_resource.blueprint[each.key].output.appId
  }

  read_query_parameters = {
    "$select" = ["id", "appId", "displayName"]
  }

  response_export_values = {
    appId = "appId"
    id    = "id"
  }
}

# -----------------------------------------------------------------------------
# Federated Identity Credentials (AKS Workload Identity)
# -----------------------------------------------------------------------------

resource "msgraph_resource" "fic" {
  for_each    = var.agents
  url         = "applications/${msgraph_resource.blueprint[each.key].output.id}/federatedIdentityCredentials"
  api_version = "beta"

  body = {
    name      = "aks-${replace(each.key, "_", "-")}"
    audiences = ["api://AzureADTokenExchange"]
    issuer    = var.aks_oidc_issuer_url
    subject   = "system:serviceaccount:${each.value.namespace}:${each.value.service_account}"
  }
}
