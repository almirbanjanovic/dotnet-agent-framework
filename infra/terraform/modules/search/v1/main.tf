# =============================================================================
# Azure AI Search Module v1
# Creates: Search Service (azurerm)
#
# The Knowledge Source is created in root main.tf (after RBAC + blob uploads)
# via the Knowledge Source API (2025-11-01-preview), which auto-generates:
#   index, data source, skillset, indexer
# Requires Standard tier or higher (semantic ranker must be available).
# =============================================================================

locals {
  search_service_name = "srch-${var.base_name}-${var.environment}-${var.location}"
}

# -----------------------------------------------------------------------------
# AI Search Service (control plane)
# -----------------------------------------------------------------------------
resource "azurerm_search_service" "this" {
  name                          = local.search_service_name
  resource_group_name           = var.resource_group_name
  location                      = var.location
  sku                           = var.sku
  semantic_search_sku           = "standard"

  allowed_ips = var.allowed_ips

  identity {
    type = "SystemAssigned"
  }

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# Retrieve Admin Keys (primary_key is write-only in AzureRM 4.63+)
# -----------------------------------------------------------------------------
data "azapi_resource_action" "list_admin_keys" {
  type        = "Microsoft.Search/searchServices@2025-05-01"
  resource_id = azurerm_search_service.this.id
  action      = "listAdminKeys"

  response_export_values = ["primaryKey"]
}

