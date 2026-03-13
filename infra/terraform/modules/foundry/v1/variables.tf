
variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
}
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
}

variable "sku_name" {
  description = "SKU for the AI Services account"
  type        = string
}

variable "deployment_sku_name" {
  description = "SKU for the model deployment (e.g., GlobalStandard, Standard)"
  type        = string
}

variable "deployment_model_format" {
  description = "Model format (e.g., OpenAI)"
  type        = string
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

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}

