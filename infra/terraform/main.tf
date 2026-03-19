#--------------------------------------------------------------------------------------------------------------------------------
# Data Sources
#--------------------------------------------------------------------------------------------------------------------------------

data "azurerm_client_config" "current" {}

# Deployer public IP — used for firewall exceptions during provisioning
data "http" "deployer_ip" {
  url = "https://api.ipify.org"
}

locals {
  # Each module composes: {prefix}-{base_name}-{environment}-{location}
  # These two values are passed separately to every module.
  name_base   = "${var.base_name}-${var.environment}"
  deployer_ip = chomp(data.http.deployer_ip.response_body)
}

#--------------------------------------------------------------------------------------------------------------------------------
# Foundry (AI Services + Model Deployments)
#--------------------------------------------------------------------------------------------------------------------------------

module "foundry" {
  source = "./modules/foundry/v1"

  base_name                = var.base_name
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

  allowed_ips = [local.deployer_ip]
  tags        = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Azure SQL Database — Operational (CRM structured data)
#--------------------------------------------------------------------------------------------------------------------------------

module "sql" {
  source = "./modules/sql/v1"

  base_name             = var.base_name
  environment           = var.environment
  location              = var.location
  resource_group_name   = var.resource_group_name
  database_name         = var.sql_database_name
  admin_login           = var.sql_admin_login
  tenant_id             = data.azurerm_client_config.current.tenant_id
  entra_admin_object_id = data.azurerm_client_config.current.object_id
  deployer_ip           = local.deployer_ip
  tags                  = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Cosmos DB — Agents (state persistence)
#--------------------------------------------------------------------------------------------------------------------------------

module "cosmosdb_agents" {
  source = "./modules/cosmosdb/v1"

  name_prefix         = "${var.base_name}-${var.environment}"
  location            = var.location
  resource_group_name = var.resource_group_name
  database_name       = var.cosmos_agents_database_name
  consistency_level   = "Eventual"
  allowed_ips         = [local.deployer_ip]
  tags                = var.tags

  containers = {
    agent_state = {
      name                  = var.cosmos_agent_state_container_name
      partition_key_paths   = ["/tenant_id", "/id"]
      partition_key_kind    = "MultiHash"
      partition_key_version = 2
    }
    conversations = {
      name                = "conversations"
      partition_key_paths = ["/sessionId"]
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
    bff        = { name = "id-bff-${local.name_base}-${var.location}" }
    crm_api    = { name = "id-crm-api-${local.name_base}-${var.location}" }
    crm_mcp    = { name = "id-crm-mcp-${local.name_base}-${var.location}" }
    know_mcp   = { name = "id-know-mcp-${local.name_base}-${var.location}" }
    crm_agent  = { name = "id-crm-agent-${local.name_base}-${var.location}" }
    prod_agent = { name = "id-prod-agent-${local.name_base}-${var.location}" }
    orch_agent = { name = "id-orch-agent-${local.name_base}-${var.location}" }
    kubelet    = { name = "id-kubelet-${local.name_base}-${var.location}" }
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# Virtual Network
#--------------------------------------------------------------------------------------------------------------------------------

module "vnet" {
  source = "./modules/vnet/v1"

  base_name           = var.base_name
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# Azure Container Registry
#--------------------------------------------------------------------------------------------------------------------------------

module "acr" {
  source = "./modules/acr/v1"

  base_name           = var.base_name
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  create_acr          = var.create_acr
  sku                 = var.acr_sku
  acr_name            = var.acr_name
  tags                = var.tags
}

#--------------------------------------------------------------------------------------------------------------------------------
# AKS (Azure CNI, system + workload node pools)
#--------------------------------------------------------------------------------------------------------------------------------

module "aks" {
  source = "./modules/aks/v1"

  base_name           = var.base_name
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  kubernetes_version  = var.aks_kubernetes_version

  system_node_vm_size = var.aks_system_node_vm_size
  system_subnet_id    = module.vnet.aks_system_subnet_id

  workload_node_vm_size = var.aks_workload_node_vm_size
  workload_subnet_id    = module.vnet.aks_workload_subnet_id

  auto_scaling_enabled = var.aks_auto_scaling_enabled
  os_disk_size_gb      = var.aks_os_disk_size_gb
  log_retention_days   = var.aks_log_retention_days

  kubelet_identity_client_id   = module.identity.identities["kubelet"].client_id
  kubelet_identity_object_id   = module.identity.identities["kubelet"].principal_id
  kubelet_identity_resource_id = module.identity.identities["kubelet"].id

  tags = var.tags

  depends_on = [module.vnet]
}

#--------------------------------------------------------------------------------------------------------------------------------
# App Gateway for Containers (AGC)
#--------------------------------------------------------------------------------------------------------------------------------

module "agc" {
  source = "./modules/agc/v1"

  base_name           = var.base_name
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  subnet_id           = module.vnet.agc_subnet_id
  tags                = var.tags

  depends_on = [module.vnet, azurerm_resource_provider_registration.service_networking]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Azure Storage — Product Images + SharePoint Documents
#--------------------------------------------------------------------------------------------------------------------------------

module "storage_images" {
  source = "./modules/storage/v1"

  base_name           = var.base_name
  purpose             = "data"
  environment         = var.environment
  resource_group_name = var.resource_group_name
  location            = var.location
  allowed_ips         = [local.deployer_ip]
  tags                = var.tags

  containers = {
    images          = { name = "product-images" }
    sharepoint_docs = { name = "sharepoint-docs" }
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# Storage Uploads — Product Images + SharePoint Documents (data plane)
# Separate from storage account creation so uploads can be ordered independently.
#--------------------------------------------------------------------------------------------------------------------------------

module "storage_uploads" {
  source = "./modules/storage-uploads/v1"

  storage_account_name = module.storage_images.name
  storage_account_id   = module.storage_images.id

  uploads = {
    images = {
      container_name = "product-images"
      source_path    = "${path.module}/../../data/contoso-images"
      file_pattern   = "*.png"
      content_type   = "image/png"
    }
    sharepoint_docs = {
      container_name = "sharepoint-docs"
      source_path    = "${path.module}/../../data/contoso-sharepoint"
      file_pattern   = "**/*.pdf"
      content_type   = "application/pdf"
    }
  }

  depends_on = [module.storage_images]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Azure AI Search — Knowledge Base (RAG vector store)
#--------------------------------------------------------------------------------------------------------------------------------

module "search" {
  source = "./modules/search/v1"

  base_name           = var.base_name
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = var.search_sku
  index_name          = var.search_index_name

  storage_account_id          = module.storage_images.id
  container_name              = "sharepoint-docs"
  openai_endpoint             = module.foundry.endpoint
  openai_embedding_deployment = module.foundry.embedding_deployment_name != null ? module.foundry.embedding_deployment_name : var.embedding_model_name
  openai_embedding_model      = var.embedding_model_name

  allowed_ips = [local.deployer_ip]
  tags        = var.tags

  depends_on = [module.storage_images]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Event Grid — Blob upload triggers AI Search indexer
#--------------------------------------------------------------------------------------------------------------------------------

module "eventgrid" {
  source = "./modules/eventgrid/v1"

  base_name           = var.base_name
  environment         = var.environment
  resource_group_name = var.resource_group_name
  location            = var.location
  storage_account_id  = module.storage_images.id
  container_name      = "sharepoint-docs"
  search_service_name = module.search.name
  search_indexer_name = azapi_resource.search_indexer.name
  search_api_key      = module.search.primary_key

  tags = var.tags

  depends_on = [azapi_resource.search_indexer]
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Foundry (Cognitive Services OpenAI User)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_foundry" {
  source = "./modules/rbac/foundry/v1"

  ai_services_account_id = module.foundry.account_id

  principal_ids = {
    crm_agent  = module.identity.identities["crm_agent"].principal_id
    prod_agent = module.identity.identities["prod_agent"].principal_id
    orch_agent = module.identity.identities["orch_agent"].principal_id
    search     = module.search.identity_principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Cosmos DB (Data Owner) — 1 account (agents)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_cosmosdb_agents" {
  source = "./modules/rbac/cosmosdb/v1"

  resource_group_name   = var.resource_group_name
  cosmosdb_account_id   = module.cosmosdb_agents.account_id
  cosmosdb_account_name = module.cosmosdb_agents.account_name

  principal_ids = {
    bff = module.identity.identities["bff"].principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - Storage (Blob Data Reader)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_storage" {
  source = "./modules/rbac/storage/v1"

  storage_account_id = module.storage_images.id

  principal_ids = {
    bff    = module.identity.identities["bff"].principal_id
    search = module.search.identity_principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# RBAC - ACR (AcrPull)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_acr" {
  source = "./modules/rbac/acr/v1"

  acr_id = module.acr.id

  principal_ids = {
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
# RBAC - AI Search (Search Index Data Reader)
#--------------------------------------------------------------------------------------------------------------------------------

module "rbac_search" {
  source = "./modules/rbac/search/v1"

  search_service_id = module.search.id

  principal_ids = {
    know_mcp = module.identity.identities["know_mcp"].principal_id
  }
}

#--------------------------------------------------------------------------------------------------------------------------------
# AI Search Indexer — Created AFTER RBAC so the search identity can access Storage + OpenAI
#
# Why this is separate from the search module:
# The indexer runs immediately on creation (and every 5 minutes). It needs:
# 1. RBAC in place — Storage Blob Data Reader + Cognitive Services OpenAI User
# 2. Blobs uploaded — so the first run indexes all existing PDFs, not an empty container
# Moving it here lets us enforce: storage uploads + RBAC → indexer.
#--------------------------------------------------------------------------------------------------------------------------------

resource "azapi_resource" "search_indexer" {
  type      = "Microsoft.Search/searchServices/indexers@2024-11-01"
  name      = "blob-indexer"
  parent_id = module.search.id

  body = {
    properties = {
      dataSourceName  = module.search.data_source_name
      targetIndexName = module.search.index_name
      skillsetName    = module.search.skillset_name
      schedule = {
        interval = "PT5M"
      }
      parameters = {
        configuration = {
          dataToExtract = "contentAndMetadata"
          parsingMode   = "default"
          imageAction   = "none"
        }
      }
      fieldMappings = [
        {
          sourceFieldName = "metadata_storage_path"
          targetFieldName = "source_file"
        }
      ]
    }
  }

  schema_validation_enabled = false

  depends_on = [
    module.rbac_storage,
    module.rbac_foundry,
    module.storage_uploads,
  ]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Workload Identity Federation (AKS OIDC → Managed Identity)
#--------------------------------------------------------------------------------------------------------------------------------

module "workload_identity" {
  source = "./modules/workload-identity/v1"

  aks_oidc_issuer_url = module.aks.oidc_issuer_url

  federations = {
    bff        = { identity_id = module.identity.identities["bff"].id, namespace = var.k8s_namespace, service_account = "sa-bff" }
    crm_api    = { identity_id = module.identity.identities["crm_api"].id, namespace = var.k8s_namespace, service_account = "sa-crm-api" }
    crm_mcp    = { identity_id = module.identity.identities["crm_mcp"].id, namespace = var.k8s_namespace, service_account = "sa-crm-mcp" }
    know_mcp   = { identity_id = module.identity.identities["know_mcp"].id, namespace = var.k8s_namespace, service_account = "sa-know-mcp" }
    crm_agent  = { identity_id = module.identity.identities["crm_agent"].id, namespace = var.k8s_namespace, service_account = "sa-crm-agent" }
    prod_agent = { identity_id = module.identity.identities["prod_agent"].id, namespace = var.k8s_namespace, service_account = "sa-prod-agent" }
    orch_agent = { identity_id = module.identity.identities["orch_agent"].id, namespace = var.k8s_namespace, service_account = "sa-orch-agent" }
  }

  depends_on = [module.aks]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Kubernetes Resources (namespace + service accounts)
# Uses kubectl provider — see providers.tf for configuration.
# Manifest templates are in manifests/ folder.
#--------------------------------------------------------------------------------------------------------------------------------

resource "kubectl_manifest" "namespace" {
  yaml_body = templatefile("${path.module}/manifests/namespace.yaml", {
    namespace = var.k8s_namespace
  })

  depends_on = [module.aks]
}

resource "kubectl_manifest" "service_accounts" {
  for_each = {
    bff        = { name = "sa-bff", client_id = module.identity.identities["bff"].client_id }
    crm_api    = { name = "sa-crm-api", client_id = module.identity.identities["crm_api"].client_id }
    crm_mcp    = { name = "sa-crm-mcp", client_id = module.identity.identities["crm_mcp"].client_id }
    know_mcp   = { name = "sa-know-mcp", client_id = module.identity.identities["know_mcp"].client_id }
    crm_agent  = { name = "sa-crm-agent", client_id = module.identity.identities["crm_agent"].client_id }
    prod_agent = { name = "sa-prod-agent", client_id = module.identity.identities["prod_agent"].client_id }
    orch_agent = { name = "sa-orch-agent", client_id = module.identity.identities["orch_agent"].client_id }
  }

  yaml_body = templatefile("${path.module}/manifests/service-account.yaml", {
    name      = each.value.name
    namespace = var.k8s_namespace
    client_id = each.value.client_id
  })

  depends_on = [kubectl_manifest.namespace]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Key Vault
#--------------------------------------------------------------------------------------------------------------------------------

module "keyvault" {
  source = "./modules/keyvault/v1"

  base_name           = var.base_name
  environment         = var.environment
  location            = var.location
  resource_group_name = var.resource_group_name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  allowed_ips         = ["${local.deployer_ip}/32"]
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
    deployer   = data.azurerm_client_config.current.object_id
    bff        = module.identity.identities["bff"].principal_id
    crm_api    = module.identity.identities["crm_api"].principal_id
    crm_mcp    = module.identity.identities["crm_mcp"].principal_id
    know_mcp   = module.identity.identities["know_mcp"].principal_id
    crm_agent  = module.identity.identities["crm_agent"].principal_id
    prod_agent = module.identity.identities["prod_agent"].principal_id
    orch_agent = module.identity.identities["orch_agent"].principal_id
  }

  # Certificates Officer: the deployer needs to create TLS certificates
  certificate_officer_principal_ids = {
    deployer = data.azurerm_client_config.current.object_id
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
    "AZURE-OPENAI-API-KEY"              = "disabled-by-policy-use-managed-identity"
    "AZURE-OPENAI-DEPLOYMENT-NAME"      = module.foundry.deployment_name
    "AZURE-OPENAI-EMBEDDING-DEPLOYMENT" = module.foundry.embedding_deployment_name != null ? module.foundry.embedding_deployment_name : ""
    "COSMOSDB-AGENTS-ENDPOINT"          = module.cosmosdb_agents.endpoint
    "COSMOSDB-AGENTS-KEY"               = nonsensitive(module.cosmosdb_agents.primary_key)
    "COSMOSDB-AGENTS-DATABASE"          = module.cosmosdb_agents.database_name
    "SQL-SERVER-FQDN"                   = module.sql.server_fqdn
    "SQL-DATABASE-NAME"                 = module.sql.database_name
    "STORAGE-IMAGES-ENDPOINT"           = module.storage_images.primary_blob_endpoint
    "STORAGE-IMAGES-ACCOUNT-NAME"       = module.storage_images.name
    "STORAGE-IMAGES-CONTAINER"          = module.storage_images.container_names["images"]
    "SEARCH-ENDPOINT"                   = module.search.endpoint
    "SEARCH-ADMIN-KEY"                  = nonsensitive(module.search.primary_key)
    "SEARCH-INDEX-NAME"                 = module.search.index_name

    # Workload identity client IDs (used by Helm at deploy time)
    "IDENTITY-BFF-CLIENT-ID"        = module.identity.identities["bff"].client_id
    "IDENTITY-CRM-API-CLIENT-ID"    = module.identity.identities["crm_api"].client_id
    "IDENTITY-CRM-MCP-CLIENT-ID"    = module.identity.identities["crm_mcp"].client_id
    "IDENTITY-KNOW-MCP-CLIENT-ID"   = module.identity.identities["know_mcp"].client_id
    "IDENTITY-CRM-AGENT-CLIENT-ID"  = module.identity.identities["crm_agent"].client_id
    "IDENTITY-PROD-AGENT-CLIENT-ID" = module.identity.identities["prod_agent"].client_id
    "IDENTITY-ORCH-AGENT-CLIENT-ID" = module.identity.identities["orch_agent"].client_id

    # Entra ID (Blazor WASM SPA authentication)
    "ENTRA-BFF-CLIENT-ID" = module.entra.bff_client_id
    "ENTRA-TENANT-ID"     = module.entra.tenant_id
    "ENTRA-BFF-HOSTNAME"  = module.agc.frontend_fqdn

    # Customer passwords (for lab use)
    "CUSTOMER-EMMA-PASSWORD"  = nonsensitive(module.entra.test_user_passwords["emma"])
    "CUSTOMER-JAMES-PASSWORD" = nonsensitive(module.entra.test_user_passwords["james"])
    "CUSTOMER-SARAH-PASSWORD" = nonsensitive(module.entra.test_user_passwords["sarah"])
    "CUSTOMER-DAVID-PASSWORD" = nonsensitive(module.entra.test_user_passwords["david"])
    "CUSTOMER-LISA-PASSWORD"  = nonsensitive(module.entra.test_user_passwords["lisa"])

    # Customer Entra object IDs (used by deploy script to link Entra users to SQL Customers table)
    "CUSTOMER-EMMA-ENTRA-OID"  = module.entra.test_user_object_ids["emma"]
    "CUSTOMER-JAMES-ENTRA-OID" = module.entra.test_user_object_ids["james"]
    "CUSTOMER-SARAH-ENTRA-OID" = module.entra.test_user_object_ids["sarah"]
    "CUSTOMER-DAVID-ENTRA-OID" = module.entra.test_user_object_ids["david"]
    "CUSTOMER-LISA-ENTRA-OID"  = module.entra.test_user_object_ids["lisa"]
  }

  depends_on = [
    module.rbac_keyvault,
    module.foundry,
    module.cosmosdb_agents,
    module.sql,
    module.storage_images,
    module.search,
    module.identity,
    module.entra,
    module.agc,
  ]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Entra ID — App Registration, Test Users, Role Assignments
#--------------------------------------------------------------------------------------------------------------------------------

module "entra" {
  source = "./modules/entra/v1"

  base_name   = var.base_name
  environment = var.environment

  redirect_uris = [
    "https://localhost:5002/authentication/login-callback",
    "https://${module.agc.frontend_fqdn}/authentication/login-callback",
  ]
}

#--------------------------------------------------------------------------------------------------------------------------------
# TLS Certificate (self-signed, stored in Key Vault for AGC TLS termination)
#--------------------------------------------------------------------------------------------------------------------------------

module "tls_cert" {
  source = "./modules/tls-cert/v1"

  cert_name    = "tls-bff-${var.environment}"
  key_vault_id = module.keyvault.id
  common_name  = module.agc.frontend_fqdn
  dns_names    = [module.agc.frontend_fqdn]

  depends_on = [module.rbac_keyvault]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Private DNS Zones (shared across all private endpoints)
#--------------------------------------------------------------------------------------------------------------------------------

module "private_dns_zones" {
  source = "./modules/private-dns-zones/v1"

  resource_group_name = var.resource_group_name
  vnet_id             = module.vnet.vnet_id
  tags                = var.tags

  zones = {
    sql              = "privatelink.database.windows.net"
    cognitiveservices = "privatelink.cognitiveservices.azure.com"
    cosmosdb         = "privatelink.documents.azure.com"
    search           = "privatelink.search.windows.net"
    blob             = "privatelink.blob.core.windows.net"
    keyvault         = "privatelink.vaultcore.azure.net"
    acr              = "privatelink.azurecr.io"
  }

  depends_on = [module.vnet]
}

#--------------------------------------------------------------------------------------------------------------------------------
# Private Endpoints
#--------------------------------------------------------------------------------------------------------------------------------

module "pe_sql" {
  source = "./modules/private-endpoint/v1"

  name               = "pe-sql-${local.name_base}-${var.location}"
  location           = var.location
  resource_group_name = var.resource_group_name
  subnet_id          = module.vnet.private_endpoints_subnet_id
  target_resource_id = module.sql.server_id
  subresource_names  = ["sqlServer"]
  dns_zone_id        = module.private_dns_zones.zone_ids["sql"]
  tags               = var.tags

  depends_on = [module.sql, module.private_dns_zones]
}

module "pe_foundry" {
  source = "./modules/private-endpoint/v1"

  name               = "pe-aif-${local.name_base}-${var.location}"
  location           = var.location
  resource_group_name = var.resource_group_name
  subnet_id          = module.vnet.private_endpoints_subnet_id
  target_resource_id = module.foundry.account_id
  subresource_names  = ["account"]
  dns_zone_id        = module.private_dns_zones.zone_ids["cognitiveservices"]
  tags               = var.tags

  depends_on = [module.foundry, module.private_dns_zones]
}

module "pe_cosmosdb" {
  source = "./modules/private-endpoint/v1"

  name               = "pe-cosmos-${local.name_base}-${var.location}"
  location           = var.location
  resource_group_name = var.resource_group_name
  subnet_id          = module.vnet.private_endpoints_subnet_id
  target_resource_id = module.cosmosdb_agents.account_id
  subresource_names  = ["Sql"]
  dns_zone_id        = module.private_dns_zones.zone_ids["cosmosdb"]
  tags               = var.tags

  depends_on = [module.cosmosdb_agents, module.private_dns_zones]
}

module "pe_search" {
  source = "./modules/private-endpoint/v1"

  name               = "pe-srch-${local.name_base}-${var.location}"
  location           = var.location
  resource_group_name = var.resource_group_name
  subnet_id          = module.vnet.private_endpoints_subnet_id
  target_resource_id = module.search.id
  subresource_names  = ["searchService"]
  dns_zone_id        = module.private_dns_zones.zone_ids["search"]
  tags               = var.tags

  depends_on = [module.search, module.private_dns_zones]
}

module "pe_storage" {
  source = "./modules/private-endpoint/v1"

  name               = "pe-st-${local.name_base}-${var.location}"
  location           = var.location
  resource_group_name = var.resource_group_name
  subnet_id          = module.vnet.private_endpoints_subnet_id
  target_resource_id = module.storage_images.id
  subresource_names  = ["blob"]
  dns_zone_id        = module.private_dns_zones.zone_ids["blob"]
  tags               = var.tags

  depends_on = [module.storage_images, module.private_dns_zones]
}

module "pe_keyvault" {
  source = "./modules/private-endpoint/v1"

  name               = "pe-kv-${local.name_base}-${var.location}"
  location           = var.location
  resource_group_name = var.resource_group_name
  subnet_id          = module.vnet.private_endpoints_subnet_id
  target_resource_id = module.keyvault.id
  subresource_names  = ["vault"]
  dns_zone_id        = module.private_dns_zones.zone_ids["keyvault"]
  tags               = var.tags

  depends_on = [module.keyvault, module.private_dns_zones]
}

module "pe_acr" {
  source = "./modules/private-endpoint/v1"

  name               = "pe-acr-${local.name_base}-${var.location}"
  location           = var.location
  resource_group_name = var.resource_group_name
  subnet_id          = module.vnet.private_endpoints_subnet_id
  target_resource_id = module.acr.id
  subresource_names  = ["registry"]
  dns_zone_id        = module.private_dns_zones.zone_ids["acr"]
  tags               = var.tags

  depends_on = [module.acr, module.private_dns_zones]
}
