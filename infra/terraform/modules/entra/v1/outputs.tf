output "bff_client_id" {
  description = "Client ID of the BFF app registration"
  value       = azuread_application.bff.client_id
}

output "bff_client_secret" {
  description = "Client secret for the BFF app registration"
  value       = azuread_application_password.bff.value
  sensitive   = true
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
  description = "Map of test user key → password"
  value       = local.user_passwords
  sensitive   = true
}

output "test_user_upns" {
  description = "Map of test user key → user principal name"
  value = {
    for key, user in azuread_user.test : key => user.user_principal_name
  }
}
