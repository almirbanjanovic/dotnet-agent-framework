variable "storage_account_id" {
  description = "Resource ID of the Azure Storage Account"
  type        = string
}

variable "principal_ids" {
  description = "Map of logical name => principal ID to grant Storage Blob Data Reader role"
  type        = map(string)
}
