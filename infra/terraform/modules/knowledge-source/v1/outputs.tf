output "name" {
  description = "Name of the knowledge source"
  value       = var.name
}

output "index_name" {
  description = "Name of the auto-generated search index"
  value       = "${var.name}-index"
}

output "indexer_name" {
  description = "Name of the auto-generated indexer"
  value       = "${var.name}-indexer"
}
