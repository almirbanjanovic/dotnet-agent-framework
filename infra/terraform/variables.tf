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

# ---------------------------------------------------------------
# Cosmos DB
# ---------------------------------------------------------------

variable "cosmos_project_name" {
  description = "Project name used in Cosmos DB resource naming"
  type        = string
  default     = "dotnetagent"
}

variable "cosmos_iteration" {
  description = "Iteration counter for Cosmos DB (avoids soft-delete collisions)"
  type        = string
  default     = "001"
}

variable "cosmos_database_name" {
  description = "Cosmos DB SQL database name"
  type        = string
  default     = "contoso"
}