variable "cert_name" {
  description = "Name of the certificate in Key Vault"
  type        = string
}

variable "key_vault_id" {
  description = "Resource ID of the Key Vault to store the certificate"
  type        = string
}

variable "common_name" {
  description = "Common name (CN) for the certificate subject"
  type        = string
}

variable "dns_names" {
  description = "Subject Alternative Names (SANs) — additional DNS names for the certificate"
  type        = list(string)
  default     = []
}
