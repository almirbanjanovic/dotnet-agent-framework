variable "resource_group_name" {
  description = "Name of the resource group to grant Contributor on"
  type        = string
}

variable "aks_control_plane_principal_id" {
  description = "Principal ID of the AKS control plane user-assigned identity"
  type        = string
}
