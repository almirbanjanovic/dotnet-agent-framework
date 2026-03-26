variable "ai_services_account_id" {
  description = "Resource ID of the AI Services (Cognitive) account"
  type        = string
}

variable "principal_ids" {
  description = "Map of logical name => principal ID to grant Cognitive Services OpenAI User role"
  type        = map(string)
}
