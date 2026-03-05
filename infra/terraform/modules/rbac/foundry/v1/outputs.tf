output "role_assignment_ids" {
  description = "Map of role assignment IDs"
  value       = { for k, v in azurerm_role_assignment.openai_user : k => v.id }
}
