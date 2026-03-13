
variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
}
variable "environment" {
  description = "Environment name used in resource naming (e.g., agentic-ai-dev)"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
}

variable "sku" {
  description = "Azure AI Search SKU (free, basic, standard, standard2, standard3)"
  type        = string
  default     = "basic"
}

variable "index_name" {
  description = "Name of the search index"
  type        = string
  default     = "knowledge-documents"
}

variable "container_name" {
  description = "Blob container name for the indexer data source"
  type        = string
}

variable "storage_account_id" {
  description = "Resource ID of the storage account for the blob data source"
  type        = string
}

variable "openai_endpoint" {
  description = "Azure OpenAI endpoint URL (for embedding skillset)"
  type        = string
}

variable "openai_embedding_deployment" {
  description = "Azure OpenAI embedding model deployment name"
  type        = string
}

variable "openai_embedding_model" {
  description = "Azure OpenAI embedding model name"
  type        = string
  default     = "text-embedding-ada-002"
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}

