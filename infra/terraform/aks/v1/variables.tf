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

variable "iteration" {
  description = "Iteration counter for naming"
  type        = string
  default     = "001"
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

variable "node_vm_size" {
  description = "VM size for the default node pool"
  type        = string
  default     = "Standard_D4s_v5"
}

variable "node_count" {
  description = "Initial node count (used when auto-scaling is disabled)"
  type        = number
  default     = 2
}

variable "auto_scaling_enabled" {
  description = "Enable cluster auto-scaler on the default node pool"
  type        = bool
  default     = true
}

variable "node_min_count" {
  description = "Minimum node count when auto-scaling is enabled"
  type        = number
  default     = 1
}

variable "node_max_count" {
  description = "Maximum node count when auto-scaling is enabled"
  type        = number
  default     = 5
}

variable "os_disk_size_gb" {
  description = "OS disk size in GB for nodes"
  type        = number
  default     = 64
}

# -----------------------------------------------------------------------------
# Kubelet Identity (user-assigned, for AcrPull etc.)
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
  default     = 30
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
