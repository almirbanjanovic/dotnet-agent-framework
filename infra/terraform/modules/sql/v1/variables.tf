
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

variable "database_name" {
  description = "Name of the SQL database"
  type        = string
  default     = "contoso-outdoors"
}

variable "admin_login" {
  description = "SQL Server administrator login name"
  type        = string
  default     = "sqladmin"
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}

variable "tenant_id" {
  description = "Azure AD tenant ID for Entra administrator"
  type        = string
}

variable "entra_admin_login" {
  description = "Display name for the Azure AD administrator"
  type        = string
  default     = "SQL Entra Admin"
}

variable "entra_admin_object_id" {
  description = "Object ID of the Azure AD user or group to set as SQL admin"
  type        = string
}

