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
# Cosmos DB — Operational
# ---------------------------------------------------------------

output "cosmosdb_operational_endpoint" {
  description = "Cosmos DB operational account endpoint"
  value       = module.cosmosdb_operational.endpoint
}

output "cosmosdb_operational_account_name" {
  description = "Cosmos DB operational account name"
  value       = module.cosmosdb_operational.account_name
}

output "cosmosdb_operational_database_name" {
  description = "Cosmos DB operational database name"
  value       = module.cosmosdb_operational.database_name
}

# ---------------------------------------------------------------
# Cosmos DB — Knowledge (RAG)
# ---------------------------------------------------------------

output "cosmosdb_knowledge_endpoint" {
  description = "Cosmos DB knowledge account endpoint"
  value       = module.cosmosdb_knowledge.endpoint
}

output "cosmosdb_knowledge_account_name" {
  description = "Cosmos DB knowledge account name"
  value       = module.cosmosdb_knowledge.account_name
}

output "cosmosdb_knowledge_database_name" {
  description = "Cosmos DB knowledge database name"
  value       = module.cosmosdb_knowledge.database_name
}

# ---------------------------------------------------------------
# Cosmos DB — Agents
# ---------------------------------------------------------------

output "cosmosdb_agents_endpoint" {
  description = "Cosmos DB agents account endpoint"
  value       = module.cosmosdb_agents.endpoint
}

output "cosmosdb_agents_account_name" {
  description = "Cosmos DB agents account name"
  value       = module.cosmosdb_agents.account_name
}

output "cosmosdb_agents_database_name" {
  description = "Cosmos DB agents database name"
  value       = module.cosmosdb_agents.database_name
}

# ---------------------------------------------------------------
# Storage — Product Images
# ---------------------------------------------------------------

output "storage_images_account_name" {
  description = "Storage account name for product images"
  value       = module.storage_images.name
}

output "storage_images_blob_endpoint" {
  description = "Primary blob endpoint for product images"
  value       = module.storage_images.primary_blob_endpoint
}

output "storage_images_container_name" {
  description = "Blob container name for product images"
  value       = module.storage_images.container_name
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
