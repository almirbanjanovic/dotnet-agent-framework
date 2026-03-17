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

variable "address_space" {
  description = "VNet address space CIDR"
  type        = string
  default     = "10.0.0.0/16"
}

variable "aks_system_subnet_cidr" {
  description = "Subnet CIDR for AKS system node pool"
  type        = string
  default     = "10.0.0.0/24"
}

variable "aks_user_subnet_cidr" {
  description = "Subnet CIDR for AKS workload node pool (application pods)"
  type        = string
  default     = "10.0.1.0/24"
}

variable "agc_subnet_cidr" {
  description = "Subnet CIDR for App Gateway for Containers"
  type        = string
  default     = "10.0.2.0/24"
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
