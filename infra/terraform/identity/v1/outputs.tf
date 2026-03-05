output "identities" {
  description = "Map of created identities with their IDs, principal IDs, and client IDs"
  value = {
    for key, identity in azurerm_user_assigned_identity.workload : key => {
      id           = identity.id
      principal_id = identity.principal_id
      client_id    = identity.client_id
      name         = identity.name
    }
  }
}
