variable "zones" {
  description = "Map of logical name => private DNS zone FQDN"
  type        = map(string)
}

variable "vnet_id" {
  description = "Virtual Network ID to link DNS zones to"
  type        = string
}

variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
