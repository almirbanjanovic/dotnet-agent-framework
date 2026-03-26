
variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
}

variable "purpose" {
  description = "Purpose suffix for storage account name (e.g., data, img) to avoid naming collisions"
  type        = string
  default     = ""
}

variable "environment" {
  description = "Environment name used in resource naming (e.g., dotnetagent-dev)"
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

variable "account_tier" {
  description = "Storage account tier (Standard, Premium)"
  type        = string
  default     = "Standard"
}

variable "replication_type" {
  description = "Storage replication type (LRS, GRS, RAGRS, ZRS)"
  type        = string
  default     = "LRS"
}

variable "allow_public_access" {
  description = "Allow public access to blobs"
  type        = bool
  default     = false
}

variable "containers" {
  description = "Map of containers to create. Each entry creates a blob container."
  type = map(object({
    name        = string
    access_type = optional(string, "private")
  }))
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

