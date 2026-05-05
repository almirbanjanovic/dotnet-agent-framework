output "bff_client_id" {
  description = "Client ID of the BFF app registration"
  value       = azuread_application.bff.client_id
}

output "tenant_id" {
  description = "Entra tenant ID"
  value       = data.azuread_client_config.current.tenant_id
}

output "domain" {
  description = "Default verified domain of the Entra tenant"
  value       = local.domain
}

output "test_user_passwords" {
  description = "Map of test user key → generated password. NOTE: For users in `imported_user_keys`, this is the password terraform GENERATED for this run, but `lifecycle.ignore_changes = [password]` on `azuread_user.test` means the user's ACTUAL password is whatever was set on the original create — these printed values do not match the live account."
  value       = local.user_passwords
  sensitive   = true
}

output "imported_user_keys" {
  description = "List of test user keys (e.g. [\"emma\", \"sarah\"]) that already existed in the tenant and were imported into state instead of created. Their passwords from `test_user_passwords` are NOT real — use the password printed by the original setup-local run, or reset via the Azure portal."
  value       = sort(keys(local.import_targets))
}

output "test_user_upns" {
  description = "Map of test user key → user principal name"
  value = {
    for key, user in azuread_user.test : key => user.user_principal_name
  }
}

output "test_user_object_ids" {
  description = "Map of test user key → Entra object ID (used to link to Cosmos DB Customers container)"
  value = {
    for key, user in azuread_user.test : key => user.object_id
  }
}

output "test_user_customer_ids" {
  description = "Map of test user key → customer ID in Cosmos DB (from variable definition)"
  value = {
    for key, user in var.test_users : key => user.customer_id
  }
}
