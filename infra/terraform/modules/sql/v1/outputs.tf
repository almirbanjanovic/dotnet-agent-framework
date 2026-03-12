output "server_id" {
  description = "SQL Server resource ID"
  value       = azurerm_mssql_server.this.id
}

output "server_name" {
  description = "SQL Server name"
  value       = azurerm_mssql_server.this.name
}

output "server_fqdn" {
  description = "SQL Server fully qualified domain name"
  value       = azurerm_mssql_server.this.fully_qualified_domain_name
}

output "database_name" {
  description = "SQL Database name"
  value       = azurerm_mssql_database.this.name
}

output "admin_login" {
  description = "SQL Server admin login"
  value       = var.admin_login
}

output "admin_password" {
  description = "SQL Server admin password"
  value       = random_password.sql_admin.result
  sensitive   = true
}
