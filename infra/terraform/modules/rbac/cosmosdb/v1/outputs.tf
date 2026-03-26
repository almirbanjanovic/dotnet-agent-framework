output "data_contributor_assignment_ids" {
  description = "Map of Data Contributor role assignment IDs"
  value       = { for k, v in azurerm_cosmosdb_sql_role_assignment.data_contributor : k => v.id }
}
