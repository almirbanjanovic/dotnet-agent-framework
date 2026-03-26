
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

variable "tenant_id" {
  description = "Azure AD tenant ID for the Key Vault"
  type        = string
}

variable "soft_delete_retention_days" {
  description = "Number of days to retain soft-deleted vaults"
  type        = number
  default     = 7
}

variable "purge_protection_enabled" {
  description = "Enable purge protection (prevents permanent deletion during retention period)"
  type        = bool
  default     = true
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}

variable "public_network_access_enabled" {
  description = "Whether public network access is enabled for the Key Vault"
  type        = bool
  default     = false
}

variable "allowed_ips" {
  description = "List of IP CIDRs allowed through the firewall (deployer IP)"
  type        = list(string)
  default     = []
}

