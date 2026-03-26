variable "search_service_id" {
  description = "Resource ID of the Azure AI Search service"
  type        = string
}

variable "principal_ids" {
  description = "Map of logical name => principal ID to assign Search Index Data Reader"
  type        = map(string)
}
