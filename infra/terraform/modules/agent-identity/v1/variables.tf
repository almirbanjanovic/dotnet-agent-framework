# =============================================================================
# Agent Identity Module v1 — Variables
# =============================================================================

variable "aks_oidc_issuer_url" {
  description = "OIDC issuer URL from the AKS cluster (for federated identity credentials)"
  type        = string
}

variable "sponsor_object_id" {
  description = "Entra object ID of the human sponsor for Agent Identity Blueprints."
  type        = string
}

variable "owner_object_id" {
  description = "Entra object ID of the human owner for Agent Identity Blueprints."
  type        = string
}

variable "agents" {
  description = <<-EOT
    Map of agent identities to create. Each entry creates:
    - An Agent Identity Blueprint (Graph beta application)
    - A Blueprint Principal (Graph beta service principal)
    - A federated identity credential (AKS workload identity)

    blueprint_display_name: Human-readable name for the blueprint (no env suffix).
                            E.g., "Contoso CRM Agent"
    namespace:              Kubernetes namespace for the service account.
    service_account:        Kubernetes service account name.
  EOT

  type = map(object({
    blueprint_display_name = string
    namespace              = string
    service_account        = string
  }))
}
