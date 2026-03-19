# =============================================================================
# Agent Identity Module v2 — Outputs
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
    for key, principal in msgraph_resource.blueprint_principal : key => {
      client_id              = principal.output.appId
      object_id              = principal.output.id
      app_object_id          = msgraph_resource.blueprint[key].output.id
      blueprint_display_name = msgraph_resource.blueprint[key].output.displayName
    }
  }
}
