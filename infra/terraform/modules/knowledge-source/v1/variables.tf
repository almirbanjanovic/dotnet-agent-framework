variable "name" {
  description = "Name of the knowledge source (auto-generates {name}-index, {name}-indexer, etc.)"
  type        = string
}

variable "search_endpoint" {
  description = "Azure AI Search endpoint URL"
  type        = string
}

variable "search_api_key" {
  description = "Azure AI Search admin API key"
  type        = string
  sensitive   = true
}

variable "storage_account_id" {
  description = "Resource ID of the storage account containing the blob data"
  type        = string
}

variable "container_name" {
  description = "Blob container name to index"
  type        = string
}

variable "openai_endpoint" {
  description = "Azure OpenAI endpoint URL"
  type        = string
}

variable "openai_embedding_deployment" {
  description = "Azure OpenAI embedding model deployment name"
  type        = string
}

variable "openai_embedding_model" {
  description = "Azure OpenAI embedding model name"
  type        = string
}
