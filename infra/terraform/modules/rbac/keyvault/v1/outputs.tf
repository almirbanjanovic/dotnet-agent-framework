output "officer_assignment_ids" {
  description = "Map of Secrets Officer role assignment IDs"
  value       = { for k, v in azurerm_role_assignment.secrets_officer : k => v.id }
}

output "reader_assignment_ids" {
  description = "Map of Secrets User role assignment IDs"
  value       = { for k, v in azurerm_role_assignment.secrets_user : k => v.id }
}
