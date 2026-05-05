output "foundry_endpoint" {
  description = "Azure OpenAI endpoint URL"
  value       = module.foundry.endpoint
}

output "chat_deployment_name" {
  description = "Name of the chat model deployment"
  value       = module.foundry.deployment_name
}

output "embedding_deployment_name" {
  description = "Name of the embedding model deployment"
  value       = module.foundry.embedding_deployment_name
}

output "tenant_id" {
  description = "Azure tenant ID (used by DefaultAzureCredential to disambiguate accounts)."
  value       = data.azurerm_client_config.current.tenant_id
}

output "bff_client_id" {
  description = "Client ID of the BFF SPA app registration created in the deployer's Entra tenant."
  value       = module.entra.bff_client_id
}

output "test_user_upns" {
  description = "Map of test user key (emma, james, ...) → user principal name. Use these to sign in to the Blazor UI."
  value       = module.entra.test_user_upns
}

output "test_user_passwords" {
  description = "Map of test user key → generated password. Marked sensitive; setup-local prints them once. For keys present in `imported_user_keys`, this value is NOT the live password (see that output's description)."
  value       = module.entra.test_user_passwords
  sensitive   = true
}

output "imported_user_keys" {
  description = "List of test user keys that pre-existed in the tenant and were imported instead of created. Their printed passwords are not real — use the password from the original setup-local run."
  value       = module.entra.imported_user_keys
}

# Pre-built JSON fragment for AzureAd:CustomerMap so setup-local can substitute
# it verbatim into appsettings.Local.json.template. Maps each test user's UPN
# (lower-cased) to the seeded customer ID in data/contoso-crm/customers.csv.
output "customer_map_json" {
  description = "JSON object mapping test-user UPNs → seeded customer IDs. Substituted into bff-api appsettings.Local.json."
  value = jsonencode({
    for key, upn in module.entra.test_user_upns :
    lower(upn) => module.entra.test_user_customer_ids[key]
  })
}
