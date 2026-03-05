output "role_assignment_id" {
  description = "AKS control plane Contributor role assignment ID"
  value       = azurerm_role_assignment.aks_contributor.id
}
