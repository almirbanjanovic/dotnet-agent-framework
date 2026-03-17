variable "name_prefix" {
  description = "Prefix for the Cosmos DB account name (e.g., project-environment)"
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
  description = "Name of the Cosmos DB SQL database"
  type        = string
}

variable "consistency_level" {
  description = "Cosmos DB consistency level"
  type        = string

  validation {
    condition     = contains(["Strong", "BoundedStaleness", "Session", "ConsistentPrefix", "Eventual"], var.consistency_level)
    error_message = "Must be one of: Strong, BoundedStaleness, Session, ConsistentPrefix, Eventual."
  }
}

variable "capabilities" {
  description = "List of Cosmos DB capabilities to enable (e.g., EnableNoSQLVectorSearch)"
  type        = list(string)
  default     = []
}

variable "containers" {
  description = "Map of containers to create. Key is a logical name."
  type = map(object({
    name                  = string
    partition_key_paths   = list(string)
    partition_key_kind    = optional(string, "Hash")
    partition_key_version = optional(number)
    indexing_policy = optional(object({
      indexing_mode  = string
      included_paths = optional(list(string), ["/*"])
      excluded_paths = optional(list(string), [])
    }))
  }))
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}

variable "public_network_access_enabled" {
  description = "Whether public network access is enabled for the Cosmos DB account"
  type        = bool
  default     = true
}
