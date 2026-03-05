variable "project_name" {
  description = "Project name used in resource naming"
  type        = string
}

variable "environment" {
  description = "Deployment environment (e.g., dev, staging, prod)"
  type        = string
}

variable "location" {
  description = "Azure region for the Cosmos DB account"
  type        = string
}

variable "resource_group_name" {
  description = "Name of the resource group to deploy into"
  type        = string
}

variable "iteration" {
  description = "Iteration counter to avoid soft-delete naming collisions"
  type        = string
  default     = "001"
}

variable "database_name" {
  description = "Name of the Cosmos DB SQL database"
  type        = string
  default     = "contoso"
}

variable "agent_state_container_name" {
  description = "Name of the agent state store container"
  type        = string
  default     = "workshop_agent_state_store"
}

variable "consistency_level" {
  description = "Cosmos DB consistency level"
  type        = string
  default     = "Session"

  validation {
    condition     = contains(["Strong", "BoundedStaleness", "Session", "ConsistentPrefix", "Eventual"], var.consistency_level)
    error_message = "Must be one of: Strong, BoundedStaleness, Session, ConsistentPrefix, Eventual."
  }
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
