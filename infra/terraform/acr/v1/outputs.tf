output "id" {
  description = "ACR resource ID"
  value       = local.acr_id
}

output "name" {
  description = "ACR name"
  value       = local.acr_name
}

output "login_server" {
  description = "ACR login server URL"
  value       = local.login_server
}
