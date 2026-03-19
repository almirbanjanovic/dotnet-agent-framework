output "zone_ids" {
  description = "Map of logical name => private DNS zone resource ID"
  value       = { for k, v in azurerm_private_dns_zone.this : k => v.id }
}
