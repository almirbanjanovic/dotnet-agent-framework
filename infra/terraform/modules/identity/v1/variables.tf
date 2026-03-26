variable "resource_group_name" {
  description = "Name of the resource group"
  type        = string
}

variable "location" {
  description = "Azure region"
  type        = string
}

variable "identities" {
  description = "Map of workload identities to create. Key is a logical name, value contains the Azure resource name."
  type = map(object({
    name = string
  }))
}

variable "tags" {
  description = "Tags to apply to all resources"
  type        = map(string)
  default     = {}
}
