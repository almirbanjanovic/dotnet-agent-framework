#--------------------------------------------------------------------------------------------------------------------------------
# Data Sources
#--------------------------------------------------------------------------------------------------------------------------------

data "azurerm_client_config" "current" {}

#--------------------------------------------------------------------------------------------------------------------------------
# Foundry (AI Services + Model Deployments)
#--------------------------------------------------------------------------------------------------------------------------------

module "foundry" {
  source = "./modules/foundry/v1"

  environment              = var.environment
  location                 = var.location
  resource_group_name      = var.resource_group_name
  account_kind             = var.cognitive_account_kind
  sku_name                 = var.oai_sku_name
  deployment_sku_name      = var.oai_deployment_sku_name
  deployment_model_format  = var.oai_deployment_model_format
  deployment_model_name    = var.oai_deployment_model_name
  deployment_model_version = var.oai_deployment_model_version
  version_upgrade_option   = var.oai_version_upgrade_option

  create_embedding_deployment = var.create_embedding_deployment
  embedding_model_name        = var.embedding_model_name
  embedding_model_version     = var.embedding_model_version
  embedding_sku_name          = var.embedding_sku_name
  embedding_capacity          = var.embedding_capacity

  tags = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Cosmos DB — Operational (CRM structured data)
#--------------------------------------------------------------------------------------------------------------------------------

module "cosmosdb_operational" {
  source = "./modules/cosmosdb/v1"

  name_prefix         = var.cosmos_project_name
  purpose             = "operational"
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  iteration           = var.cosmos_iteration
  database_name       = var.cosmos_operational_database_name
  consistency_level   = "Session"
  tags                = var.tags

  containers = {
    customers       = { name = "Customers",      partition_key_paths = ["/id"] }
    orders          = { name = "Orders",         partition_key_paths = ["/customer_id"] }
    order_items     = { name = "OrderItems",     partition_key_paths = ["/order_id"] }
    products        = { name = "Products",       partition_key_paths = ["/category"] }
    promotions      = { name = "Promotions",     partition_key_paths = ["/id"] }
    support_tickets = { name = "SupportTickets", partition_key_paths = ["/customer_id"] }
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# Cosmos DB — Knowledge (RAG vector store)
#--------------------------------------------------------------------------------------------------------------------------------

module "cosmosdb_knowledge" {
  source = "./modules/cosmosdb/v1"

  name_prefix         = var.cosmos_project_name
  purpose             = "knowledge"
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  iteration           = var.cosmos_iteration
  database_name       = var.cosmos_knowledge_database_name
  consistency_level   = "Eventual"
  capabilities        = ["EnableNoSQLVectorSearch"]
  tags                = var.tags

  containers = {
    knowledge_documents = {
      name                = "KnowledgeDocuments"
      partition_key_paths = ["/id"]
      indexing_policy = {
        indexing_mode  = "consistent"
        included_paths = ["/*"]
        excluded_paths = ["/content_vector/*"]
      }
    }
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# Cosmos DB — Agents (state persistence)
#--------------------------------------------------------------------------------------------------------------------------------

module "cosmosdb_agents" {
  source = "./modules/cosmosdb/v1"

  name_prefix         = var.cosmos_project_name
  purpose             = "agents"
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  iteration           = var.cosmos_iteration
  database_name       = var.cosmos_agents_database_name
  consistency_level   = "Eventual"
  tags                = var.tags

  containers = {
    agent_state = {
      name                  = var.cosmos_agent_state_container_name
      partition_key_paths   = ["/tenant_id", "/id"]
      partition_key_kind    = "MultiHash"
      partition_key_version = 2
    }
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# Workload Identities
#--------------------------------------------------------------------------------------------------------------------------------

module "identity" {
  source = "./modules/identity/v1"

  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags

  identities = {
    backend = { name = "uami-backend-${var.environment}-${var.iteration}" }
    kubelet = { name = "uami-kubelet-${var.environment}-${var.iteration}" }
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# Azure Container Registry
#--------------------------------------------------------------------------------------------------------------------------------

module "acr" {
  source = "./modules/acr/v1"

  project_name        = var.acr_project_name
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  iteration           = var.iteration
  create_acr          = var.create_acr
  sku                 = var.acr_sku
  existing_acr_name   = var.existing_acr_name
  tags                = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# AKS
#--------------------------------------------------------------------------------------------------------------------------------

module "aks" {
  source = "./modules/aks/v1"

  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  iteration           = var.iteration
  kubernetes_version  = var.aks_kubernetes_version
  node_vm_size        = var.aks_node_vm_size
  node_count          = var.aks_node_count
  auto_scaling_enabled = var.aks_auto_scaling_enabled
  node_min_count      = var.aks_node_min_count
  node_max_count      = var.aks_node_max_count
  os_disk_size_gb     = var.aks_os_disk_size_gb
  log_retention_days  = var.aks_log_retention_days

  kubelet_identity_client_id    = module.identity.identities["kubelet"].client_id
  kubelet_identity_object_id    = module.identity.identities["kubelet"].principal_id
  kubelet_identity_resource_id  = module.identity.identities["kubelet"].id

  tags = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Foundry (Cognitive Services OpenAI User)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_foundry" {
  source = "./modules/rbac/foundry/v1"

  ai_services_account_id = module.foundry.account_id

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Cosmos DB (Data Owner + Data Contributor) — all 3 accounts
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_cosmosdb_operational" {
  source = "./modules/rbac/cosmosdb/v1"

  resource_group_name   = var.resource_group_name
  cosmosdb_account_id   = module.cosmosdb_operational.account_id
  cosmosdb_account_name = module.cosmosdb_operational.account_name

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
  }
}

module "rbac_cosmosdb_knowledge" {
  source = "./modules/rbac/cosmosdb/v1"

  resource_group_name   = var.resource_group_name
  cosmosdb_account_id   = module.cosmosdb_knowledge.account_id
  cosmosdb_account_name = module.cosmosdb_knowledge.account_name

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
  }
}

module "rbac_cosmosdb_agents" {
  source = "./modules/rbac/cosmosdb/v1"

  resource_group_name   = var.resource_group_name
  cosmosdb_account_id   = module.cosmosdb_agents.account_id
  cosmosdb_account_name = module.cosmosdb_agents.account_name

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - ACR (AcrPull)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_acr" {
  source = "./modules/rbac/acr/v1"

  acr_id = module.acr.id

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
    kubelet = module.identity.identities["kubelet"].principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - AKS Control Plane (Contributor on resource group)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_aks" {
  source = "./modules/rbac/aks/v1"

  resource_group_name              = var.resource_group_name
  aks_control_plane_principal_id   = module.aks.control_plane_identity_principal_id
}

#--------------------------------------------------------------------------------------------------------------------------------
# Key Vault
#--------------------------------------------------------------------------------------------------------------------------------

module "keyvault" {
  source = "./modules/keyvault/v1"

  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  iteration           = var.iteration
  tags                = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Key Vault
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_keyvault" {
  source = "./modules/rbac/keyvault/v1"

  keyvault_id = module.keyvault.id

  # Secrets Officer: the deployer (Terraform SP or current user) needs to write secrets
  officer_principal_ids = {
    deployer = data.azurerm_client_config.current.object_id
  }

  # Secrets User: workload identities and the deployer need to read secrets
  reader_principal_ids = {
    deployer = data.azurerm_client_config.current.object_id
    backend  = module.identity.identities["backend"].principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# Key Vault Secrets
#--------------------------------------------------------------------------------------------------------------------------------

module "keyvault_secrets" {
  source = "./modules/keyvault-secrets/v1"

  key_vault_id = module.keyvault.id

  secrets = {
    "AZURE-OPENAI-ENDPOINT"             = module.foundry.endpoint
    "AZURE-OPENAI-API-KEY"              = module.foundry.primary_key
    "AZURE-OPENAI-DEPLOYMENT-NAME"      = module.foundry.deployment_name
    "AZURE-OPENAI-EMBEDDING-DEPLOYMENT" = module.foundry.embedding_deployment_name != null ? module.foundry.embedding_deployment_name : ""
    "COSMOSDB-OPERATIONAL-ENDPOINT"     = module.cosmosdb_operational.endpoint
    "COSMOSDB-OPERATIONAL-KEY"          = module.cosmosdb_operational.primary_key
    "COSMOSDB-OPERATIONAL-DATABASE"     = module.cosmosdb_operational.database_name
    "COSMOSDB-KNOWLEDGE-ENDPOINT"       = module.cosmosdb_knowledge.endpoint
    "COSMOSDB-KNOWLEDGE-KEY"            = module.cosmosdb_knowledge.primary_key
    "COSMOSDB-KNOWLEDGE-DATABASE"       = module.cosmosdb_knowledge.database_name
    "COSMOSDB-AGENTS-ENDPOINT"          = module.cosmosdb_agents.endpoint
    "COSMOSDB-AGENTS-KEY"               = module.cosmosdb_agents.primary_key
    "COSMOSDB-AGENTS-DATABASE"          = module.cosmosdb_agents.database_name
  }

  depends_on = [module.rbac_keyvault]
}