
variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
}
variable "environment" {
  description = "Deployment environment"
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

variable "create_acr" {
  description = "Create a new ACR. Set to false to use an existing one."
  type        = bool
  default     = true
}

variable "sku" {
  description = "ACR SKU (Basic, Standard, Premium)"
  type        = string

  validation {
    condition     = contains(["Basic", "Standard", "Premium"], var.sku)
    error_message = "ACR SKU must be Basic, Standard, or Premium."
  }
}

variable "admin_enabled" {
  description = "Enable admin user on ACR"
  type        = bool
  default     = true
}

variable "acr_name" {
  description = "Name of the ACR (used when create_acr = false)"
  type        = string
  default     = ""
}

variable "existing_acr_resource_group" {
  description = "Resource group of existing ACR (only used when create_acr = false)"
  type        = string
  default     = ""
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}

