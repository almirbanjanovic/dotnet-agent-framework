variable "environment" {
  description = "Environment name used in resource naming"
  type        = string
}

variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "storage_account_id" {
  description = "Resource ID of the storage account to monitor"
  type        = string
}

variable "container_name" {
  description = "Blob container name to filter events on"
  type        = string
}

variable "search_service_name" {
  description = "Name of the AI Search service (for webhook URL)"
  type        = string
}

variable "search_indexer_name" {
  description = "Name of the AI Search indexer to trigger"
  type        = string
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
