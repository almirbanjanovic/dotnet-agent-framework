# =============================================================================
# Cosmos DB Module v1
# Creates: One account, one database, N containers (configurable)
# Reusable — call once per account (operational, knowledge, agents, etc.)
# =============================================================================

locals {
  account_name  = lower("cosmos-${var.name_prefix}-${var.location}")
  database_name = var.database_name
}

# -----------------------------------------------------------------------------
# Cosmos DB Account
# -----------------------------------------------------------------------------
resource "azurerm_cosmosdb_account" "this" {
  name                = local.account_name
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

  dynamic "capabilities" {
    for_each = var.capabilities
    content {
      name = capabilities.value
    }
  }

  local_authentication_disabled = false
  public_network_access_enabled = var.public_network_access_enabled

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# SQL Database
# -----------------------------------------------------------------------------
resource "azurerm_cosmosdb_sql_database" "this" {
  name                = local.database_name
  resource_group_name = var.resource_group_name
  account_name        = azurerm_cosmosdb_account.this.name
}

# -----------------------------------------------------------------------------
# Containers (dynamic from variable)
# -----------------------------------------------------------------------------
resource "azurerm_cosmosdb_sql_container" "this" {
  for_each = var.containers

  name                  = each.value.name
  resource_group_name   = var.resource_group_name
  account_name          = azurerm_cosmosdb_account.this.name
  database_name         = azurerm_cosmosdb_sql_database.this.name
  partition_key_paths   = each.value.partition_key_paths
  partition_key_kind    = each.value.partition_key_kind
  partition_key_version = each.value.partition_key_version

  dynamic "indexing_policy" {
    for_each = lookup(each.value, "indexing_policy", null) != null ? [each.value.indexing_policy] : []
    content {
      indexing_mode = indexing_policy.value.indexing_mode

      dynamic "included_path" {
        for_each = lookup(indexing_policy.value, "included_paths", ["/*"])
        content {
          path = included_path.value
        }
      }

      dynamic "excluded_path" {
        for_each = lookup(indexing_policy.value, "excluded_paths", [])
        content {
          path = excluded_path.value
        }
      }
    }
  }
}
