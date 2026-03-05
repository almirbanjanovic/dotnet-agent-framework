#--------------------------------------------------------------------------------------------------------------------------------
# Foundry (AI Services + Model Deployments)
#--------------------------------------------------------------------------------------------------------------------------------

module "foundry" {
  source = "./foundry/v1"

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
# Cosmos DB
#--------------------------------------------------------------------------------------------------------------------------------

module "cosmosdb" {
  source = "./cosmosdb/v1"

  project_name        = var.cosmos_project_name
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  iteration           = var.cosmos_iteration
  database_name       = var.cosmos_database_name
  tags                = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Workload Identities
#--------------------------------------------------------------------------------------------------------------------------------

module "identity" {
  source = "./identity/v1"

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
  source = "./acr/v1"

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
  source = "./aks/v1"

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

  kubelet_identity_client_id    = module.identity.identities["kubelet"].client_id
  kubelet_identity_object_id    = module.identity.identities["kubelet"].principal_id
  kubelet_identity_resource_id  = module.identity.identities["kubelet"].id

  tags = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Foundry (Cognitive Services OpenAI User)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_foundry" {
  source = "./rbac/foundry/v1"

  ai_services_account_id = module.foundry.account_id

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Cosmos DB (Data Owner + Data Contributor)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_cosmosdb" {
  source = "./rbac/cosmosdb/v1"

  resource_group_name  = var.resource_group_name
  cosmosdb_account_id   = module.cosmosdb.account_id
  cosmosdb_account_name = module.cosmosdb.account_name

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - ACR (AcrPull)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_acr" {
  source = "./rbac/acr/v1"

  acr_id = module.acr.id

  principal_ids = {
    backend = module.identity.identities["backend"].principal_id
    kubelet = module.identity.identities["kubelet"].principal_id
  }
}