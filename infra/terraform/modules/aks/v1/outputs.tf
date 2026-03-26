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

output "kube_config_client_certificate" {
  description = "Base64-encoded client certificate for Kubernetes auth"
  value       = azurerm_kubernetes_cluster.this.kube_config[0].client_certificate
  sensitive   = true
}

output "kube_config_client_key" {
  description = "Base64-encoded client key for Kubernetes auth"
  value       = azurerm_kubernetes_cluster.this.kube_config[0].client_key
  sensitive   = true
}

output "kube_config_cluster_ca" {
  description = "Base64-encoded cluster CA certificate for Kubernetes auth"
  value       = azurerm_kubernetes_cluster.this.kube_config[0].cluster_ca_certificate
  sensitive   = true
}

output "oidc_issuer_url" {
  description = "OIDC issuer URL for workload identity federation"
  value       = azurerm_kubernetes_cluster.this.oidc_issuer_url
}

output "control_plane_identity_principal_id" {
  description = "Principal ID of the AKS control plane user-assigned identity"
  value       = azurerm_user_assigned_identity.aks.principal_id
}

output "fqdn" {
  description = "FQDN of the AKS cluster (from dns_prefix)"
  value       = azurerm_kubernetes_cluster.this.fqdn
}

output "node_resource_group" {
  description = "Auto-generated resource group for AKS node resources"
  value       = azurerm_kubernetes_cluster.this.node_resource_group
}

output "log_analytics_workspace_id" {
  description = "Log Analytics workspace ID used by AKS"
  value       = azurerm_log_analytics_workspace.this.id
}
