#--------------------------------------------------------------------------------------------------------------------------------
# Data Sources
#--------------------------------------------------------------------------------------------------------------------------------

data "azurerm_client_config" "current" {}

locals {
  # Composite name used for Azure resource naming: {base_name}-{environment}
  # Modules append their own prefix and suffix (e.g., aif-{name_base}-{location})
  # Result: aif-agentic-ai-dev-centralus, kv-agentic-ai-dev-001, etc.
  name_base = "${var.base_name}-${var.environment}"
}

#--------------------------------------------------------------------------------------------------------------------------------
# Foundry (AI Services + Model Deployments)
#--------------------------------------------------------------------------------------------------------------------------------

module "foundry" {
  source = "./modules/foundry/v1"

  environment              = local.name_base
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
# Azure SQL Database — Operational (CRM structured data)
#--------------------------------------------------------------------------------------------------------------------------------

module "sql" {
  source = "./modules/sql/v1"

  environment         = local.name_base
  location            = var.location
  resource_group_name = var.resource_group_name
  database_name       = var.sql_database_name
  admin_login         = var.sql_admin_login
  tags                = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Cosmos DB — Agents (state persistence)
#--------------------------------------------------------------------------------------------------------------------------------

module "cosmosdb_agents" {
  source = "./modules/cosmosdb/v1"

  name_prefix         = var.cosmos_project_name
  purpose             = "agents"
  location            = var.location
  resource_group_name = var.resource_group_name
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
    backend = { name = "uami-backend-${local.name_base}" }
    kubelet = { name = "uami-kubelet-${local.name_base}" }
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# Azure Container Registry
#--------------------------------------------------------------------------------------------------------------------------------

module "acr" {
  source = "./modules/acr/v1"

  project_name        = var.acr_project_name
  environment         = local.name_base
  location            = var.location
  resource_group_name = var.resource_group_name
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

  environment          = local.name_base
  location             = var.location
  resource_group_name  = var.resource_group_name
  kubernetes_version   = var.aks_kubernetes_version
  node_vm_size         = var.aks_node_vm_size
  node_count           = var.aks_node_count
  auto_scaling_enabled = var.aks_auto_scaling_enabled
  node_min_count       = var.aks_node_min_count
  node_max_count       = var.aks_node_max_count
  os_disk_size_gb      = var.aks_os_disk_size_gb
  log_retention_days   = var.aks_log_retention_days

  kubelet_identity_client_id   = module.identity.identities["kubelet"].client_id
  kubelet_identity_object_id   = module.identity.identities["kubelet"].principal_id
  kubelet_identity_resource_id = module.identity.identities["kubelet"].id

  tags = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Azure Storage — Product Images + SharePoint Documents
#--------------------------------------------------------------------------------------------------------------------------------

module "storage_images" {
  source = "./modules/storage/v1"

  project_name        = var.storage_project_name
  purpose             = "images"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags

  containers = {
    images = {
      name                = "product-images"
      upload_source_path  = "${path.module}/../../data/contoso-images"
      upload_file_pattern = "*.png"
      upload_content_type = "image/png"
    }
    sharepoint_docs = {
      name                = "sharepoint-docs"
      upload_source_path  = "${path.module}/../../data/contoso-sharepoint"
      upload_file_pattern = "**/*.pdf"
      upload_content_type = "application/pdf"
    }
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# Azure AI Search — Knowledge Base (RAG vector store)
#--------------------------------------------------------------------------------------------------------------------------------

module "search" {
  source = "./modules/search/v1"

  environment         = local.name_base
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = var.search_sku
  index_name          = var.search_index_name

  storage_account_id          = module.storage_images.id
  container_name              = "sharepoint-docs"
  openai_endpoint             = module.foundry.endpoint
  openai_embedding_deployment = module.foundry.embedding_deployment_name != null ? module.foundry.embedding_deployment_name : var.embedding_model_name

  tags = var.tags

  depends_on = [module.storage_images, module.rbac_storage]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Event Grid — Blob upload triggers AI Search indexer
#--------------------------------------------------------------------------------------------------------------------------------

module "eventgrid" {
  source = "./modules/eventgrid/v1"

  environment         = local.name_base
  resource_group_name = var.resource_group_name
  location            = var.location
  storage_account_id  = module.storage_images.id
  container_name      = "sharepoint-docs"
  search_service_name = module.search.name
  search_indexer_name = module.search.indexer_name

  tags = var.tags

  depends_on = [module.search]
}

#--------------------------------------------------------------------------------------------------------------------------------
# CRM Data Seeding (local-exec — runs dotnet seed-data tool)
#--------------------------------------------------------------------------------------------------------------------------------

resource "null_resource" "seed_crm" {
  triggers = {
    data_hash = sha256(join("", [for f in fileset("${path.module}/../../data/contoso-crm", "*.csv") :
      filesha256("${path.module}/../../data/contoso-crm/${f}")]))
  }

  provisioner "local-exec" {
    command     = "dotnet run --project ${path.module}/../../src/seed-data"
    working_dir = path.module

    environment = {
      SQL_SERVER_FQDN    = module.sql.server_fqdn
      SQL_DATABASE_NAME  = module.sql.database_name
      SQL_ADMIN_LOGIN    = module.sql.admin_login
      SQL_ADMIN_PASSWORD = nonsensitive(module.sql.admin_password)
    }
  }

  depends_on = [module.sql]
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Foundry (Cognitive Services OpenAI User)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_foundry" {
  source = "./modules/rbac/foundry/v1"

  ai_services_account_id = module.foundry.account_id

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
    search  = module.search.identity_principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Cosmos DB (Data Owner + Data Contributor) — 1 account (agents)
#--------------------------------------------------------------------------------------------------------------------------------

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
# RBAC - Storage (Blob Data Reader)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_storage" {
  source = "./modules/rbac/storage/v1"

  storage_account_id = module.storage_images.id

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
    search  = module.search.identity_principal_id
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

  resource_group_name            = var.resource_group_name
  aks_control_plane_principal_id = module.aks.control_plane_identity_principal_id
}

#--------------------------------------------------------------------------------------------------------------------------------
# Key Vault
#--------------------------------------------------------------------------------------------------------------------------------

module "keyvault" {
  source = "./modules/keyvault/v1"

  environment         = local.name_base
  location            = var.location
  resource_group_name = var.resource_group_name
  tenant_id           = data.azurerm_client_config.current.tenant_id
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
    "COSMOSDB-AGENTS-ENDPOINT"          = module.cosmosdb_agents.endpoint
    "COSMOSDB-AGENTS-KEY"               = module.cosmosdb_agents.primary_key
    "COSMOSDB-AGENTS-DATABASE"          = module.cosmosdb_agents.database_name
    "SQL-SERVER-FQDN"                   = module.sql.server_fqdn
    "SQL-DATABASE-NAME"                 = module.sql.database_name
    "SQL-ADMIN-LOGIN"                   = module.sql.admin_login
    "SQL-ADMIN-PASSWORD"                = module.sql.admin_password
    "STORAGE-IMAGES-ENDPOINT"           = module.storage_images.primary_blob_endpoint
    "STORAGE-IMAGES-ACCOUNT-NAME"       = module.storage_images.name
    "STORAGE-IMAGES-CONTAINER"          = module.storage_images.container_names["images"]
    "STORAGE-IMAGES-KEY"                = module.storage_images.primary_access_key
    "SEARCH-ENDPOINT"                   = module.search.endpoint
    "SEARCH-ADMIN-KEY"                  = module.search.primary_key
    "SEARCH-INDEX-NAME"                 = module.search.index_name
  }

  depends_on = [module.rbac_keyvault]
}