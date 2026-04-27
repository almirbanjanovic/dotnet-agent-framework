variable "location" {
  description = "Azure region for the local-dev resources"
  type        = string
  default     = "centralus"
}

variable "resource_group_name" {
  description = "Resource group name. Defaults to rg-<base_name>-<environment>. Override via TF_VAR_resource_group_name."
  type        = string
  default     = null
}

variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
  default     = "dotnetagent"
}

variable "environment" {
  description = "Deployment environment tag"
  type        = string
  default     = "localdev"
}

variable "chat_model_name" {
  description = "Chat model to deploy"
  type        = string
  default     = "gpt-4.1"
}

variable "chat_model_version" {
  description = "Chat model version"
  type        = string
  default     = "2025-04-14"
}

variable "embedding_model_name" {
  description = "Embedding model to deploy"
  type        = string
  default     = "text-embedding-3-small"
}

variable "embedding_model_version" {
  description = "Embedding model version"
  type        = string
  default     = "1"
}
