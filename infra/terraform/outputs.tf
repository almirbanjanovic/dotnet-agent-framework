// Learning/dev convenience outputs.
// Do not expose sensitive values via Terraform outputs in production.

# ---------------------------------------------------------------
# Foundry (AI Services)
# ---------------------------------------------------------------

output "openai_endpoint" {
  description = "Azure OpenAI endpoint URL for SDK usage"
  value       = module.foundry.endpoint
}

output "openai_api_key" {
  description = "Azure OpenAI API key"
  value       = nonsensitive(module.foundry.primary_key)
}

output "openai_deployment_name" {
  description = "Chat model deployment name"
  value       = module.foundry.deployment_name
}

output "embedding_deployment_name" {
  description = "Embedding model deployment name"
  value       = module.foundry.embedding_deployment_name
}

# ---------------------------------------------------------------
# Cosmos DB
# ---------------------------------------------------------------

output "cosmosdb_endpoint" {
  description = "Cosmos DB endpoint URL"
  value       = module.cosmosdb.endpoint
}

output "cosmosdb_account_name" {
  description = "Cosmos DB account name"
  value       = module.cosmosdb.account_name
}

output "cosmosdb_database_name" {
  description = "Cosmos DB database name"
  value       = module.cosmosdb.database_name
}

# ---------------------------------------------------------------
# ACR
# ---------------------------------------------------------------

output "acr_name" {
  description = "Azure Container Registry name"
  value       = module.acr.name
}

output "acr_login_server" {
  description = "ACR login server URL"
  value       = module.acr.login_server
}

# ---------------------------------------------------------------
# AKS
# ---------------------------------------------------------------

output "aks_cluster_name" {
  description = "AKS cluster name"
  value       = module.aks.cluster_name
}

output "aks_oidc_issuer_url" {
  description = "AKS OIDC issuer URL for workload identity"
  value       = module.aks.oidc_issuer_url
}

# ---------------------------------------------------------------
# Identities
# ---------------------------------------------------------------

output "backend_identity_client_id" {
  description = "Client ID of the backend workload identity"
  value       = module.identity.identities["backend"].client_id
}

output "kubelet_identity_client_id" {
  description = "Client ID of the kubelet identity"
  value       = module.identity.identities["kubelet"].client_id
}

# ---------------------------------------------------------------
# Key Vault
# ---------------------------------------------------------------

output "keyvault_name" {
  description = "Key Vault name"
  value       = module.keyvault.name
}

output "keyvault_uri" {
  description = "Key Vault URI (use this in appsettings.json)"
  value       = module.keyvault.vault_uri
}
