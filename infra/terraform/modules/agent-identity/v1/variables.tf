# =============================================================================
# Agent Identity Module v1 — Variables
# =============================================================================

variable "environment" {
  description = "Deployment environment (e.g., dev, staging, prod). Used in agent identity tags."
  type        = string
}

variable "aks_oidc_issuer_url" {
  description = "OIDC issuer URL from the AKS cluster (for federated identity credentials)"
  type        = string
}

variable "agents" {
  description = <<-EOT
    Map of agent identities to create. Each entry creates:
    - An application registration (blueprint)
    - A service principal (agent identity)
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
