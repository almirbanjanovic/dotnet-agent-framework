variable "resource_group_name" {
  description = "Name of the resource group containing the managed identities"
  type        = string
}

variable "aks_oidc_issuer_url" {
  description = "OIDC issuer URL from the AKS cluster"
  type        = string
}

variable "federations" {
  description = "Map of federated identity credentials. Key is the credential name."
  type = map(object({
    identity_id     = string
    namespace       = string
    service_account = string
  }))
}
