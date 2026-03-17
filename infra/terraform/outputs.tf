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
# Azure SQL Database — Operational (CRM)
# ---------------------------------------------------------------

output "sql_server_fqdn" {
  description = "Azure SQL Server fully qualified domain name"
  value       = module.sql.server_fqdn
}

output "sql_database_name" {
  description = "Azure SQL Database name"
  value       = module.sql.database_name
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
  value       = module.storage_images.container_names["images"]
}

# ---------------------------------------------------------------
# AI Search
# ---------------------------------------------------------------

output "search_endpoint" {
  description = "Azure AI Search endpoint URL"
  value       = module.search.endpoint
}

output "search_name" {
  description = "Azure AI Search service name"
  value       = module.search.name
}

output "search_index_name" {
  description = "Azure AI Search index name"
  value       = module.search.index_name
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

output "aks_fqdn" {
  description = "AKS cluster FQDN (for ingress and Entra redirect URI)"
  value       = local.aks_fqdn
}

# ---------------------------------------------------------------
# Identities
# ---------------------------------------------------------------

output "bff_identity_client_id" {
  description = "Client ID of the BFF workload identity"
  value       = module.identity.identities["bff"].client_id
}

output "crm_api_identity_client_id" {
  description = "Client ID of the CRM API workload identity"
  value       = module.identity.identities["crm_api"].client_id
}

output "crm_mcp_identity_client_id" {
  description = "Client ID of the CRM MCP workload identity"
  value       = module.identity.identities["crm_mcp"].client_id
}

output "know_mcp_identity_client_id" {
  description = "Client ID of the Knowledge MCP workload identity"
  value       = module.identity.identities["know_mcp"].client_id
}

output "crm_agent_identity_client_id" {
  description = "Client ID of the CRM Agent workload identity"
  value       = module.identity.identities["crm_agent"].client_id
}

output "prod_agent_identity_client_id" {
  description = "Client ID of the Product Agent workload identity"
  value       = module.identity.identities["prod_agent"].client_id
}

output "orch_agent_identity_client_id" {
  description = "Client ID of the Orchestrator Agent workload identity"
  value       = module.identity.identities["orch_agent"].client_id
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

# ---------------------------------------------------------------
# Entra ID
# ---------------------------------------------------------------

output "entra_bff_client_id" {
  description = "Entra app registration client ID for BFF"
  value       = module.entra.bff_client_id
}

output "entra_tenant_id" {
  description = "Entra tenant ID"
  value       = module.entra.tenant_id
}

output "entra_domain" {
  description = "Entra default verified domain"
  value       = module.entra.domain
}

output "entra_test_user_upns" {
  description = "Test user principal names (login emails)"
  value       = module.entra.test_user_upns
}

# ---------------------------------------------------------------
# TLS
# ---------------------------------------------------------------

output "tls_cert_secret_id" {
  description = "Key Vault secret ID for the TLS certificate (used by AKS ingress)"
  value       = module.tls_cert.versionless_secret_id
}
