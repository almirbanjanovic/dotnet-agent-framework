variable "environment" {
  description = "Deployment environment (e.g., agentic-ai)"
  type        = string
}

variable "location" {
  description = "Azure region for the AI Services account"
  type        = string
}

variable "resource_group_name" {
  description = "Name of the resource group to deploy into"
  type        = string
}

variable "account_kind" {
  description = "Cognitive account kind (e.g., AIServices, OpenAI)"
  type        = string
  default     = "AIServices"
}

variable "sku_name" {
  description = "SKU for the AI Services account"
  type        = string
  default     = "S0"
}

variable "deployment_sku_name" {
  description = "SKU for the model deployment (e.g., GlobalStandard, Standard)"
  type        = string
  default     = "GlobalStandard"
}

variable "deployment_model_format" {
  description = "Model format (e.g., OpenAI)"
  type        = string
  default     = "OpenAI"
}

variable "deployment_model_name" {
  description = "Model name to deploy (e.g., gpt-4.1)"
  type        = string
}

variable "deployment_model_version" {
  description = "Model version"
  type        = string
}

variable "version_upgrade_option" {
  description = "Version upgrade option (e.g., NoAutoUpgrade)"
  type        = string
  default     = "NoAutoUpgrade"
}

# -----------------------------------------------------------------------------
# Embedding Deployment
# -----------------------------------------------------------------------------

variable "create_embedding_deployment" {
  description = "Whether to create the embedding model deployment"
  type        = bool
  default     = true
}

variable "embedding_model_name" {
  description = "Embedding model name (e.g., text-embedding-ada-002)"
  type        = string
  default     = "text-embedding-ada-002"
}

variable "embedding_model_version" {
  description = "Embedding model version"
  type        = string
  default     = "2"
}

variable "embedding_sku_name" {
  description = "SKU for the embedding deployment"
  type        = string
  default     = "Standard"
}

variable "embedding_capacity" {
  description = "Capacity (TPM in thousands) for embedding deployment"
  type        = number
  default     = 10
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
