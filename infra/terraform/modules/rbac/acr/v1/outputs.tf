output "role_assignment_ids" {
  description = "Map of AcrPull role assignment IDs"
  value       = { for k, v in azurerm_role_assignment.acr_pull : k => v.id }
}
