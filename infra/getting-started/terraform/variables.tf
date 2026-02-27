variable "tags" {
  type = map(string)
}

variable "resource_group_name" {
  description = "Resource group name"
  type        = string
}

variable "environment" {
  description = "Environment (e.g., getting-started)"
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

variable "oai_deployment_model_name" {
  description = "Azure OpenAI model name"
  type        = string
}

variable "oai_deployment_model_version" {
  description = "Azure OpenAI model version"
  type        = string
}