# =============================================================================
# Agent Identity Module v1 — Outputs
# =============================================================================

output "agents" {
  description = <<-EOT
    Map of agent identity details. Each entry contains:
    - client_id:    Application (client) ID — used for K8s service account annotation
    - object_id:    Service principal object ID — used for Azure RBAC assignments
    - app_object_id: Application object ID — the blueprint's directory object
    - blueprint_display_name: Human-readable blueprint name
  EOT

  value = {
    for key, agent in azuread_service_principal.agent : key => {
      client_id              = agent.client_id
      object_id              = agent.object_id
      app_object_id          = azuread_application.blueprint[key].object_id
      blueprint_display_name = azuread_application.blueprint[key].display_name
    }
  }
}
