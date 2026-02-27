variable "tags" {
  type = map(string)
}

variable "resource_group_name" {
  description = "Resource group name"
  type        = string
}

variable "base_name" {
  type        = string
  default = "getting-started"
}

variable "environment" {
  description = "Environment (e.g., dev, prod, etc.)"
  type        = string
  default     = "dev"
}

variable "location" {
  description = "Azure location"
  type        = string
  default     = "centralus"
}