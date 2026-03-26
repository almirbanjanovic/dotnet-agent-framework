variable "keyvault_id" {
  description = "Resource ID of the Key Vault"
  type        = string
}

variable "officer_principal_ids" {
  description = "Map of logical name => principal ID to grant Key Vault Secrets Officer (write access)"
  type        = map(string)
}

variable "reader_principal_ids" {
  description = "Map of logical name => principal ID to grant Key Vault Secrets User (read access)"
  type        = map(string)
}

variable "certificate_officer_principal_ids" {
  description = "Map of logical name => principal ID to grant Key Vault Certificates Officer"
  type        = map(string)
  default     = {}
}
