# =============================================================================
# AKS Module v1
# Creates: Log Analytics workspace, AKS cluster (Azure CNI) with system + workload node pools
# =============================================================================

locals {
  cluster_name   = "aks-${var.base_name}-${var.environment}-${var.location}"
  workspace_name = "log-${var.base_name}-${var.environment}-${var.location}"
}

# -----------------------------------------------------------------------------
# Log Analytics Workspace (for AKS monitoring)
# -----------------------------------------------------------------------------
resource "azurerm_log_analytics_workspace" "this" {
  name                = local.workspace_name
  location            = var.location
  resource_group_name = var.resource_group_name
  sku                 = "PerGB2018"
  retention_in_days   = var.log_retention_days
  tags                = var.tags
}

# -----------------------------------------------------------------------------
# User-Assigned Identity for AKS control plane
# -----------------------------------------------------------------------------
resource "azurerm_user_assigned_identity" "aks" {
  name                = "uami-aks-${var.environment}"
  resource_group_name = var.resource_group_name
  location            = var.location
  tags                = var.tags
}

# -----------------------------------------------------------------------------
# AKS Cluster (Azure CNI, system node pool only)
# -----------------------------------------------------------------------------
resource "azurerm_kubernetes_cluster" "this" {
  name                = local.cluster_name
  location            = var.location
  resource_group_name = var.resource_group_name
  dns_prefix          = var.dns_prefix != "" ? var.dns_prefix : local.cluster_name
  kubernetes_version  = var.kubernetes_version

  default_node_pool {
    name                         = "system"
    vm_size                      = var.system_node_vm_size
    node_count                   = var.system_node_count
    auto_scaling_enabled         = var.auto_scaling_enabled
    min_count                    = var.auto_scaling_enabled ? var.system_node_min_count : null
    max_count                    = var.auto_scaling_enabled ? var.system_node_max_count : null
    os_disk_size_gb              = var.os_disk_size_gb
    vnet_subnet_id               = var.system_subnet_id
    only_critical_addons_enabled = true

    upgrade_settings {
      drain_timeout_in_minutes      = 0
      max_surge                     = "10%"
      node_soak_duration_in_minutes = 0
    }
  }

  network_profile {
    network_plugin = "azure"
    network_policy = "azure"
    service_cidr   = var.service_cidr
    dns_service_ip = var.dns_service_ip
  }

  identity {
    type         = "UserAssigned"
    identity_ids = [azurerm_user_assigned_identity.aks.id]
  }

  # Kubelet identity — enables RBAC for node pools (e.g., AcrPull)
  kubelet_identity {
    client_id                 = var.kubelet_identity_client_id
    object_id                 = var.kubelet_identity_object_id
    user_assigned_identity_id = var.kubelet_identity_resource_id
  }

  oidc_issuer_enabled       = var.oidc_issuer_enabled
  workload_identity_enabled = var.workload_identity_enabled

  oms_agent {
    log_analytics_workspace_id = azurerm_log_analytics_workspace.this.id
  }

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# Workload Node Pool (application pods — all app containers run here)
# -----------------------------------------------------------------------------
resource "azurerm_kubernetes_cluster_node_pool" "workload" {
  name                  = "workload"
  kubernetes_cluster_id = azurerm_kubernetes_cluster.this.id
  vm_size               = var.workload_node_vm_size
  node_count            = var.workload_node_count
  auto_scaling_enabled  = var.auto_scaling_enabled
  min_count             = var.auto_scaling_enabled ? var.workload_node_min_count : null
  max_count             = var.auto_scaling_enabled ? var.workload_node_max_count : null
  os_disk_size_gb       = var.os_disk_size_gb
  vnet_subnet_id        = var.workload_subnet_id
  mode                  = "User"

  upgrade_settings {
    drain_timeout_in_minutes      = 0
    max_surge                     = "10%"
    node_soak_duration_in_minutes = 0
  }

  tags = var.tags
}

