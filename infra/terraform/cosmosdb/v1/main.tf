# =============================================================================
# Cosmos DB Module v1
# Creates: Account, SQL Database, and all application containers
# =============================================================================

locals {
  cosmos_db_name             = lower("${var.project_name}-${var.environment}-cosmos-${var.iteration}")
  cosmos_database_name       = var.database_name
  agent_state_container_name = var.agent_state_container_name
}

# -----------------------------------------------------------------------------
# Cosmos DB Account
# -----------------------------------------------------------------------------
resource "azurerm_cosmosdb_account" "this" {
  name                = local.cosmos_db_name
  location            = var.location
  resource_group_name = var.resource_group_name
  offer_type          = "Standard"
  kind                = "GlobalDocumentDB"

  consistency_policy {
    consistency_level = var.consistency_level
  }

  geo_location {
    location          = var.location
    failover_priority = 0
    zone_redundant    = false
  }

  capabilities {
    name = "EnableNoSQLVectorSearch"
  }

  local_authentication_disabled = false
  public_network_access_enabled = true

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# SQL Database
# -----------------------------------------------------------------------------
resource "azurerm_cosmosdb_sql_database" "this" {
  name                = local.cosmos_database_name
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
}

# -----------------------------------------------------------------------------
# Containers
# -----------------------------------------------------------------------------

resource "azurerm_cosmosdb_sql_container" "customers" {
  name                = "Customers"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/id"]

  indexing_policy {
    indexing_mode = "consistent"

    included_path {
      path = "/*"
    }
  }
}

resource "azurerm_cosmosdb_sql_container" "subscriptions" {
  name                = "Subscriptions"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/customer_id"]
}

resource "azurerm_cosmosdb_sql_container" "products" {
  name                = "Products"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/category"]
}

resource "azurerm_cosmosdb_sql_container" "promotions" {
  name                = "Promotions"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/id"]
}

resource "azurerm_cosmosdb_sql_container" "invoices" {
  name                = "Invoices"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/subscription_id"]
}

resource "azurerm_cosmosdb_sql_container" "payments" {
  name                = "Payments"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/invoice_id"]
}

resource "azurerm_cosmosdb_sql_container" "security_logs" {
  name                = "SecurityLogs"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/customer_id"]
}

resource "azurerm_cosmosdb_sql_container" "orders" {
  name                = "Orders"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/customer_id"]
}

resource "azurerm_cosmosdb_sql_container" "support_tickets" {
  name                = "SupportTickets"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/customer_id"]
}

resource "azurerm_cosmosdb_sql_container" "data_usage" {
  name                = "DataUsage"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/subscription_id"]
}

resource "azurerm_cosmosdb_sql_container" "service_incidents" {
  name                = "ServiceIncidents"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/subscription_id"]
}

resource "azurerm_cosmosdb_sql_container" "knowledge_documents" {
  name                = "KnowledgeDocuments"
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
  database_name       = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths = ["/id"]

  indexing_policy {
    indexing_mode = "consistent"

    included_path {
      path = "/*"
    }

    excluded_path {
      path = "/content_vector/*"
    }
  }
}

resource "azurerm_cosmosdb_sql_container" "agent_state" {
  name                  = local.agent_state_container_name
  resource_group_name   = var.resource_group_name
  account_name          = azurerm_cosmosdb_account.this.name
  database_name         = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths   = ["/tenant_id", "/id"]
  partition_key_kind    = "MultiHash"
  partition_key_version = 2
}
