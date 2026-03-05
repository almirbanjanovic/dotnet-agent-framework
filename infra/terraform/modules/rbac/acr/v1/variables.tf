variable "acr_id" {
  description = "Resource ID of the Azure Container Registry"
  type        = string
}

variable "principal_ids" {
  description = "Map of logical name => principal ID to grant AcrPull role"
  type        = map(string)
}
