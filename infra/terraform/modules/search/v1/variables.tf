
variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
}
variable "environment" {
  description = "Environment name used in resource naming (e.g., agentic-ai-dev)"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
}

variable "sku" {
  description = "Azure AI Search SKU (standard, standard2, standard3)"
  type        = string
  default     = "standard"
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}

variable "allowed_ips" {
  description = "List of IP addresses allowed through the firewall (deployer IP)"
  type        = list(string)
  default     = []
}

