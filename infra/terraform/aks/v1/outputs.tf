output "cluster_id" {
  description = "AKS cluster resource ID"
  value       = azurerm_kubernetes_cluster.this.id
}

output "cluster_name" {
  description = "AKS cluster name"
  value       = azurerm_kubernetes_cluster.this.name
}

output "kube_config_raw" {
  description = "Raw kubeconfig for the AKS cluster"
  value       = azurerm_kubernetes_cluster.this.kube_config_raw
  sensitive   = true
}

output "kube_config_host" {
  description = "Kubernetes API server host"
  value       = azurerm_kubernetes_cluster.this.kube_config[0].host
}

output "oidc_issuer_url" {
  description = "OIDC issuer URL for workload identity federation"
  value       = azurerm_kubernetes_cluster.this.oidc_issuer_url
}

output "control_plane_identity_principal_id" {
  description = "Principal ID of the AKS control plane user-assigned identity"
  value       = azurerm_user_assigned_identity.aks.principal_id
}

output "node_resource_group" {
  description = "Auto-generated resource group for AKS node resources"
  value       = azurerm_kubernetes_cluster.this.node_resource_group
}

output "log_analytics_workspace_id" {
  description = "Log Analytics workspace ID used by AKS"
  value       = azurerm_log_analytics_workspace.this.id
}
