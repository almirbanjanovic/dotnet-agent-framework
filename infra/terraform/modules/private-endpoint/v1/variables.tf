variable "name" {
  description = "Name of the private endpoint"
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

variable "subnet_id" {
  description = "Subnet ID for the private endpoint"
  type        = string
}

variable "target_resource_id" {
  description = "Resource ID of the target service"
  type        = string
}

variable "subresource_names" {
  description = "List of subresource names (e.g. sqlServer, blob, vault)"
  type        = list(string)
}

variable "dns_zone_id" {
  description = "Private DNS zone resource ID (from private-dns-zones module)"
  type        = string
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
