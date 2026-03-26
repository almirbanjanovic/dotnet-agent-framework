variable "storage_account_name" {
  description = "Name of the storage account to upload files to"
  type        = string
}

variable "storage_account_id" {
  description = "Resource ID of the storage account (used for RBAC assignment)"
  type        = string
}

variable "uploads" {
  description = "Map of upload configurations. Each entry uploads files matching file_pattern from source_path into the named container."
  type = map(object({
    container_name = string
    source_path    = string
    file_pattern   = string
    content_type   = string
  }))
}
