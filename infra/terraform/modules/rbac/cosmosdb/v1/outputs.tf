output "data_owner_assignment_ids" {
  description = "Map of Data Owner role assignment IDs"
  value       = { for k, v in azurerm_cosmosdb_sql_role_assignment.data_owner : k => v.id }
}

output "data_contributor_assignment_ids" {
  description = "Map of Data Contributor role assignment IDs"
  value       = { for k, v in azurerm_cosmosdb_sql_role_assignment.data_contributor : k => v.id }
}
