output "blob_count" {
  description = "Total number of blobs uploaded"
  value       = length(azurerm_storage_blob.this)
}
