variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
}

variable "environment" {
  description = "Deployment environment"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
}

variable "kubernetes_version" {
  description = "Kubernetes version (e.g., 1.31). Leave null for latest."
  type        = string
  default     = null
}

variable "dns_prefix" {
  description = "DNS prefix for the AKS cluster. Defaults to the cluster name."
  type        = string
  default     = ""
}

# -----------------------------------------------------------------------------
# System Node Pool
# -----------------------------------------------------------------------------

variable "system_node_vm_size" {
  description = "VM size for the system node pool"
  type        = string
}

variable "system_node_count" {
  description = "Initial node count for the system pool"
  type        = number
  default     = 1
}

variable "system_node_min_count" {
  description = "Minimum node count for system pool when auto-scaling"
  type        = number
  default     = 1
}

variable "system_node_max_count" {
  description = "Maximum node count for system pool when auto-scaling"
  type        = number
  default     = 3
}

variable "system_subnet_id" {
  description = "Subnet ID for system node pool (Azure CNI)"
  type        = string
}

# -----------------------------------------------------------------------------
# User Node Pool (application workloads)
# -----------------------------------------------------------------------------

variable "user_node_vm_size" {
  description = "VM size for the user node pool"
  type        = string
}

variable "user_node_count" {
  description = "Initial node count for the user pool"
  type        = number
  default     = 1
}

variable "user_node_min_count" {
  description = "Minimum node count for user pool when auto-scaling"
  type        = number
  default     = 1
}

variable "user_node_max_count" {
  description = "Maximum node count for user pool when auto-scaling"
  type        = number
  default     = 5
}

variable "user_subnet_id" {
  description = "Subnet ID for user node pool (Azure CNI)"
  type        = string
}

# -----------------------------------------------------------------------------
# Common
# -----------------------------------------------------------------------------

variable "auto_scaling_enabled" {
  description = "Enable cluster auto-scaler on node pools"
  type        = bool
  default     = true
}

variable "os_disk_size_gb" {
  description = "OS disk size in GB for nodes"
  type        = number
}

variable "service_cidr" {
  description = "Kubernetes service CIDR (must not overlap with VNet)"
  type        = string
  default     = "10.1.0.0/16"
}

variable "dns_service_ip" {
  description = "DNS service IP (must be within service_cidr)"
  type        = string
  default     = "10.1.0.10"
}

# -----------------------------------------------------------------------------
# Kubelet Identity
# -----------------------------------------------------------------------------

variable "kubelet_identity_client_id" {
  description = "Client ID of the user-assigned identity for kubelet"
  type        = string
}

variable "kubelet_identity_object_id" {
  description = "Object (principal) ID of the user-assigned identity for kubelet"
  type        = string
}

variable "kubelet_identity_resource_id" {
  description = "Full resource ID of the user-assigned identity for kubelet"
  type        = string
}

# -----------------------------------------------------------------------------
# Workload Identity / OIDC
# -----------------------------------------------------------------------------

variable "oidc_issuer_enabled" {
  description = "Enable OIDC issuer for workload identity federation"
  type        = bool
  default     = true
}

variable "workload_identity_enabled" {
  description = "Enable workload identity on the cluster"
  type        = bool
  default     = true
}

# -----------------------------------------------------------------------------
# Monitoring
# -----------------------------------------------------------------------------

variable "log_retention_days" {
  description = "Log Analytics workspace retention in days"
  type        = number
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}