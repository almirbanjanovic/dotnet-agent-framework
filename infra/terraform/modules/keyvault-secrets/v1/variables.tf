variable "key_vault_id" {
  description = "Resource ID of the Key Vault to write secrets to"
  type        = string
}

variable "secrets" {
  description = "Map of secret name => secret value to store in Key Vault"
  type        = map(string)
}
