output "vnet_id" {
  description = "Virtual Network resource ID"
  value       = azurerm_virtual_network.this.id
}

output "vnet_name" {
  description = "Virtual Network name"
  value       = azurerm_virtual_network.this.name
}

output "aks_system_subnet_id" {
  description = "Subnet ID for AKS system node pool"
  value       = azurerm_subnet.aks_system.id
}

output "aks_workload_subnet_id" {
  description = "Subnet ID for AKS workload node pool"
  value       = azurerm_subnet.aks_workload.id
}

output "agc_subnet_id" {
  description = "Subnet ID for App Gateway for Containers"
  value       = azurerm_subnet.agc.id
}

output "private_endpoints_subnet_id" {
  description = "Subnet ID for private endpoints"
  value       = azurerm_subnet.private_endpoints.id
}
