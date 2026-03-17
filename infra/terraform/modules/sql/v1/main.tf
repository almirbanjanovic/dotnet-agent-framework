# =============================================================================
# Azure SQL Database Module v1
# Creates: SQL Server (logical) + Database (Serverless) + Firewall Rule
# =============================================================================

locals {
  server_name = "sql-${var.base_name}-${var.environment}-${var.location}"
}

# -----------------------------------------------------------------------------
# Random password for SQL admin
# -----------------------------------------------------------------------------
resource "random_password" "sql_admin" {
  length           = 32
  special          = true
  override_special = "!@#$%&*()-_=+"
  min_lower        = 4
  min_upper        = 4
  min_numeric      = 4
  min_special      = 2
}

# -----------------------------------------------------------------------------
# SQL Server (logical)
# -----------------------------------------------------------------------------
resource "azurerm_mssql_server" "this" {
  name                         = local.server_name
  resource_group_name          = var.resource_group_name
  location                     = var.location
  version                      = "12.0"
  administrator_login          = var.admin_login
  administrator_login_password = random_password.sql_admin.result

  minimum_tls_version           = "1.2"
  public_network_access_enabled = true

  azuread_administrator {
    login_username = var.entra_admin_login
    object_id      = var.entra_admin_object_id
    tenant_id      = var.tenant_id
  }

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# SQL Database (Serverless — auto-pause, pay-per-use)
# -----------------------------------------------------------------------------
resource "azurerm_mssql_database" "this" {
  name      = var.database_name
  server_id = azurerm_mssql_server.this.id

  sku_name                    = "GP_S_Gen5_1"
  min_capacity                = 0.5
  auto_pause_delay_in_minutes = 60
  max_size_gb                 = 2

  zone_redundant = false

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# Firewall — Allow Azure services
# -----------------------------------------------------------------------------
resource "azurerm_mssql_firewall_rule" "allow_azure" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.this.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

