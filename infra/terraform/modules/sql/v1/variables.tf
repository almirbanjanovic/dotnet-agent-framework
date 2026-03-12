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
