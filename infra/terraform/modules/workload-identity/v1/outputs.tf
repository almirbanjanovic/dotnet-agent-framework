output "federation_ids" {
  description = "Map of logical key => federated identity credential resource ID"
  value       = { for k, v in azurerm_federated_identity_credential.this : k => v.id }
}
