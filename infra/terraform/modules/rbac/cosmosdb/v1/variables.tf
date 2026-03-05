variable "resource_group_name" {
  description = "Resource group containing the Cosmos DB account"
  type        = string
}

variable "cosmosdb_account_id" {
  description = "Resource ID of the Cosmos DB account"
  type        = string
}

variable "cosmosdb_account_name" {
  description = "Name of the Cosmos DB account"
  type        = string
}

variable "principal_ids" {
  description = "Map of logical name => principal ID to grant Cosmos DB Data Owner + Data Contributor roles"
  type        = map(string)
}
