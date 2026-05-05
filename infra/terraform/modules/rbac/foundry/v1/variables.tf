variable "ai_services_account_id" {
  description = "Resource ID of the AI Services (Cognitive) account"
  type        = string
}

variable "foundry_project_id" {
  description = "Resource ID of the default Foundry project under the AI Services account. Required for the project-scoped 'Azure AI User' role assignment."
  type        = string
}

variable "principal_ids" {
  description = "Map of logical name => principal ID to grant Cognitive Services OpenAI User role"
  type        = map(string)
}
