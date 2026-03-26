output "role_assignment_ids" {
  description = "Map of Storage Blob Data Reader role assignment IDs"
  value       = { for k, v in azurerm_role_assignment.blob_data_reader : k => v.id }
}
