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

variable "iteration" {
  description = "Iteration counter for naming"
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

variable "container_name" {
  description = "Name of the blob container"
  type        = string
}

variable "container_access_type" {
  description = "Access type for the container (private, blob, container)"
  type        = string
  default     = "private"
}

variable "image_source_path" {
  description = "Local path to the folder containing image files to upload. Set to empty string to skip uploads."
  type        = string
  default     = ""
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
