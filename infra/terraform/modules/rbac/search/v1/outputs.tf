output "role_assignment_ids" {
  description = "Map of logical key => role assignment resource ID"
  value       = { for k, v in azurerm_role_assignment.search_index_data_reader : k => v.id }
}
