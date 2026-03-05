variable "tags" {
  type = map(string)
}

variable "resource_group_name" {
  description = "Resource group name"
  type        = string
}

variable "environment" {
  description = "Environment (e.g., agentic-ai)"
  type        = string
}

variable "location" {
  description = "Azure location"
  type        = string
}

variable "iteration" {
  description = "Global iteration counter for naming"
  type        = string
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
# Cosmos DB
# ---------------------------------------------------------------

variable "cosmos_project_name" {
  description = "Project name used in Cosmos DB resource naming"
  type        = string
}

variable "cosmos_iteration" {
  description = "Iteration counter for Cosmos DB (avoids soft-delete collisions)"
  type        = string
}

variable "cosmos_database_name" {
  description = "Cosmos DB SQL database name"
  type        = string
}

variable "cosmos_agent_state_container_name" {
  description = "Name of the Cosmos DB agent state store container"
  type        = string
}

# ---------------------------------------------------------------
# ACR
# ---------------------------------------------------------------

variable "acr_project_name" {
  description = "Project name used in ACR naming"
  type        = string
}

variable "create_acr" {
  description = "Create a new ACR. Set to false to use an existing one."
  type        = bool
}

variable "acr_sku" {
  description = "ACR SKU (Basic, Standard, Premium)"
  type        = string
}

variable "existing_acr_name" {
  description = "Name of existing ACR (only used when create_acr = false)"
  type        = string
}

# ---------------------------------------------------------------
# AKS
# ---------------------------------------------------------------

variable "aks_kubernetes_version" {
  description = "Kubernetes version. Leave null for latest."
  type        = string
}

variable "aks_node_vm_size" {
  description = "VM size for AKS default node pool"
  type        = string
}

variable "aks_node_count" {
  description = "Initial node count"
  type        = number
}

variable "aks_auto_scaling_enabled" {
  description = "Enable cluster auto-scaler"
  type        = bool
}

variable "aks_node_min_count" {
  description = "Minimum node count when auto-scaling"
  type        = number
}

variable "aks_node_max_count" {
  description = "Maximum node count when auto-scaling"
  type        = number
}

variable "aks_os_disk_size_gb" {
  description = "OS disk size in GB for AKS nodes"
  type        = number
}

variable "aks_log_retention_days" {
  description = "Log Analytics workspace retention in days"
  type        = number
}