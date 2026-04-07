# ---------------------------------------------------------------
# msgraph provider credentials (Agent Identity)
# Set via TF_VAR_msgraph_client_* by deploy scripts.
# Left empty in CI/CD where OIDC handles auth.
# ---------------------------------------------------------------

variable "msgraph_client_id" {
  description = "Client ID for msgraph provider (SP for Agent Identity). Set by deploy script."
  type        = string
  default     = ""
}

variable "msgraph_client_secret" {
  description = "Client secret for msgraph provider. Temporary, created by deploy script."
  type        = string
  default     = ""
  sensitive   = true
}

variable "msgraph_tenant_id" {
  description = "Tenant ID for msgraph provider."
  type        = string
  default     = ""
}

variable "tags" {
  description = "Additional tags to apply to all resources (merged with default_tags)"
  type        = map(string)
}

variable "resource_group_name" {
  description = "Resource group name"
  type        = string
}

variable "environment" {
  description = "Logical environment name (e.g., dev, staging, prod)"
  type        = string

  validation {
    condition     = contains(["dev", "staging", "production"], var.environment)
    error_message = "Environment must be dev, staging, or production."
  }
}

variable "base_name" {
  description = "Base name used in Azure resource naming (e.g., agentic-ai). Appears in resource group, Key Vault, AKS, etc."
  type        = string
}

variable "location" {
  description = "Azure location"
  type        = string

  validation {
    condition = contains([
      "eastus", "eastus2", "centralus", "northcentralus", "southcentralus",
      "westus", "westus2", "westus3", "canadacentral", "canadaeast",
      "brazilsouth", "westeurope", "northeurope", "francecentral",
      "germanywestcentral", "norwayeast", "swedencentral", "switzerlandnorth",
      "uksouth", "italynorth", "spaincentral", "eastasia", "southeastasia",
      "australiaeast", "japaneast", "koreacentral", "centralindia",
      "uaenorth", "qatarcentral", "southafricanorth",
    ], var.location)
    error_message = "Location must be a supported Azure Foundry region."
  }
}

# ---------------------------------------------------------------
# Foundry (AI Services)
# ---------------------------------------------------------------

variable "cognitive_account_kind" {
  description = "Cognitive account kind"
  type        = string
}

variable "oai_sku_name" {
  description = "Azure OpenAI account SKU name"
  type        = string
}

variable "oai_deployment_sku_name" {
  description = "Azure OpenAI model deployment SKU name"
  type        = string
}

variable "oai_deployment_model_format" {
  description = "Azure OpenAI model format"
  type        = string
}

variable "oai_deployment_model_name" {
  description = "Azure OpenAI model name"
  type        = string
}

variable "oai_deployment_model_version" {
  description = "Azure OpenAI model version"
  type        = string
}

variable "oai_version_upgrade_option" {
  description = "Azure OpenAI version upgrade option"
  type        = string
}

variable "create_embedding_deployment" {
  description = "Whether to create the embedding model deployment"
  type        = bool
}

variable "embedding_model_name" {
  description = "Embedding model name"
  type        = string
}

variable "embedding_model_version" {
  description = "Embedding model version"
  type        = string
}

variable "embedding_sku_name" {
  description = "SKU for the embedding deployment"
  type        = string
}

variable "embedding_capacity" {
  description = "Capacity (TPM in thousands) for embedding deployment"
  type        = number
}

# ---------------------------------------------------------------
# Cosmos DB (agents session state)
# ---------------------------------------------------------------

variable "cosmos_agents_database_name" {
  description = "Database name for the agents (state) Cosmos DB account"
  type        = string
}

variable "cosmos_agent_state_container_name" {
  description = "Name of the agent state store container"
  type        = string
}

# ---------------------------------------------------------------
# Cosmos DB (CRM operational data)
# ---------------------------------------------------------------

variable "cosmos_crm_database_name" {
  description = "Database name for the CRM Cosmos DB account"
  type        = string
  default     = "contoso-crm"
}

# ---------------------------------------------------------------
# AI Search
# ---------------------------------------------------------------

variable "search_sku" {
  description = "Azure AI Search SKU (free, basic, standard)"
  type        = string
  default     = "standard"

  validation {
    condition     = contains(["free", "basic", "standard", "standard2", "standard3"], var.search_sku)
    error_message = "Search SKU must be free, basic, standard, standard2, or standard3."
  }
}

variable "search_index_name" {
  description = "Name of the AI Search index for knowledge documents"
  type        = string
  default     = "knowledge-documents"
}

# ---------------------------------------------------------------
# ACR
# ---------------------------------------------------------------

variable "create_acr" {
  description = "Create a new ACR. Set to false to use an existing one."
  type        = bool
}

variable "acr_sku" {
  description = "ACR SKU (Basic, Standard, Premium)"
  type        = string
}

variable "acr_name" {
  description = "Name of the ACR (used when create_acr = false)"
  type        = string
}

# ---------------------------------------------------------------
# AKS
# ---------------------------------------------------------------

variable "aks_kubernetes_version" {
  description = "Kubernetes version for AKS cluster (major.minor, e.g. '1.30'). Pinned to prevent silent upgrades."
  type        = string
  default     = "1.30"

  validation {
    condition     = can(regex("^[0-9]+\\.[0-9]+$", var.aks_kubernetes_version))
    error_message = "Kubernetes version must be in X.Y format (e.g., 1.34)."
  }
}

variable "aks_system_node_vm_size" {
  description = "VM size for AKS system node pool"
  type        = string
}

variable "aks_workload_node_vm_size" {
  description = "VM size for AKS workload node pool (application workloads)"
  type        = string
}

variable "aks_auto_scaling_enabled" {
  description = "Enable cluster auto-scaler"
  type        = bool
}

variable "aks_os_disk_size_gb" {
  description = "OS disk size in GB for AKS nodes"
  type        = number
}

variable "aks_log_retention_days" {
  description = "Log Analytics workspace retention in days"
  type        = number
}

# ---------------------------------------------------------------
# Workload Identity
# ---------------------------------------------------------------

variable "k8s_namespace" {
  description = "Kubernetes namespace where application workloads are deployed"
  type        = string
  default     = "contoso"
}

# ---------------------------------------------------------------
# Idempotent Import Support
# Detected by deploy scripts / CI; empty defaults mean "create new".
# ---------------------------------------------------------------

variable "existing_user_ids" {
  description = "Map of test user key → Entra object ID for existing users (import instead of create)"
  type        = map(string)
  default     = {}
}

variable "import_service_networking_id" {
  description = "Azure resource ID for Microsoft.ServiceNetworking provider if already registered"
  type        = string
  default     = ""
}

variable "agent_identity_sponsor_id" {
  description = "SP object ID to set as Agent Identity Blueprint sponsor (required by Graph beta API)"
  type        = string
  default     = ""
}