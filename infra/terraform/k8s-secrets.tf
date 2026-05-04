# =============================================================================
# Kubernetes Secret bootstrap — `keyvault-secrets`
# =============================================================================
# Every per-service Helm chart (crm-api, crm-mcp, knowledge-mcp, crm-agent,
# product-agent, orchestrator-agent, bff-api) renders an env block that
# references `secretKeyRef.name: keyvault-secrets`. This file populates that
# Secret by reading the canonical secret values from the Contoso Key Vault
# (which `module.keyvault_secrets` has already written) and projecting them
# into a single namespaced Kubernetes Secret.
#
# Naming contract:
#   PascalCase--Hierarchy in Key Vault  →  same key in K8s Secret  →
#   Section__Key env var on the pod     →  Section:Key in .NET configuration
#
# Trade-off: this is a Terraform-driven, write-once bootstrap. There is no
# auto-rotation. For workshop labs that's acceptable. Production would use the
# Azure Key Vault Provider for Secrets Store CSI Driver (AKS addon
# `key_vault_secrets_provider`) plus a SecretProviderClass per workload.
# =============================================================================

locals {
  # Map of K8s Secret data keys → canonical Key Vault secret names. Every
  # value referenced here MUST already exist in `module.keyvault_secrets.secrets`.
  keyvault_secret_keys = [
    "AzureAd--TenantId",
    "AzureAd--BffClientId",
    "CosmosDb--AgentsEndpoint",
    "CosmosDb--CrmEndpoint",
    "Storage--ImagesEndpoint",
    "Storage--ImagesContainer",
    "Foundry--Endpoint",
    "Foundry--DeploymentName",
    "Foundry--EmbeddingDeploymentName",
    "Search--Endpoint",
  ]
}

data "azurerm_key_vault_secret" "for_pods" {
  for_each     = toset(local.keyvault_secret_keys)
  name         = each.value
  key_vault_id = module.keyvault.id

  # Must wait until the keyvault-secrets module has written every secret.
  depends_on = [module.keyvault_secrets]
}

resource "kubernetes_secret_v1" "keyvault_secrets" {
  metadata {
    name      = "keyvault-secrets"
    namespace = var.k8s_namespace
    labels = {
      "app.kubernetes.io/managed-by" = "terraform"
      "app.kubernetes.io/part-of"    = "contoso-outdoors"
    }
  }

  type = "Opaque"

  data = {
    for key, secret in data.azurerm_key_vault_secret.for_pods :
    key => secret.value
  }

  depends_on = [
    kubectl_manifest.namespace,
    module.keyvault_secrets,
  ]
}
