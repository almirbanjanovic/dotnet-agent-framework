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

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
