variable "project_name" {
  description = "Project name used in storage account naming"
  type        = string
}

variable "purpose" {
  description = "Purpose suffix for the storage account (e.g., images)"
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
  description = "Map of containers to create. Each entry creates a blob container and optionally uploads files."
  type = map(object({
    name                = string
    access_type         = optional(string, "private")
    upload_source_path  = optional(string, "")
    upload_file_pattern = optional(string, "*.png")
    upload_content_type = optional(string, "image/png")
  }))
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
