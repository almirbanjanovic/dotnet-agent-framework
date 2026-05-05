# The single endpoint exposed to apps. Best-practice for the new Foundry
# experience (Azure.AI.Projects 2.x): every consumer constructs an
# `AIProjectClient` from this URL and derives chat/embedding/agent clients
# from it via the project's connection-discovery APIs. We do NOT separately
# expose the account endpoint — there's no scenario in this codebase where a
# direct `AzureOpenAIClient(<account>)` call is preferable to going through
# the project.
output "foundry_project_endpoint" {
  description = "Foundry project endpoint (https://<account>.services.ai.azure.com/api/projects/<project>). Single canonical endpoint for AIProjectClient — derive ChatClient/EmbeddingClient/AgentAdministrationClient from it."
  value       = module.foundry.project_endpoint
}

output "foundry_project_name" {
  description = "Name of the default Foundry project created under the AI Services account."
  value       = module.foundry.project_name
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
  description = "Map of test user key → generated password. Marked sensitive; setup-local prints them once. Always reflects the live password — setup-local deletes orphan users before each apply, so every user is freshly created with the password printed."
  value       = module.entra.test_user_passwords
  sensitive   = true
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
