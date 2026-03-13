# =============================================================================
# Event Grid Module v1
# Creates: System Topic on Storage Account + Event Subscription for AI Search
# =============================================================================

locals {
  system_topic_name = "evgt-${var.base_name}-${var.environment}-${var.location}"
}

# -----------------------------------------------------------------------------
# Event Grid System Topic (on Storage Account)
# -----------------------------------------------------------------------------
resource "azurerm_eventgrid_system_topic" "this" {
  name                = local.system_topic_name
  resource_group_name = var.resource_group_name
  location            = var.location
  source_resource_id  = var.storage_account_id
  topic_type          = "Microsoft.Storage.StorageAccounts"

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# Event Subscription — BlobCreated → AI Search Indexer
# Uses azurerm_eventgrid_system_topic_event_subscription for proper integration
# -----------------------------------------------------------------------------
resource "azurerm_eventgrid_system_topic_event_subscription" "blob_created" {
  name                = "blob-created-to-search"
  system_topic        = azurerm_eventgrid_system_topic.this.name
  resource_group_name = var.resource_group_name

  webhook_endpoint {
    url = "https://${var.search_service_name}.search.windows.net/indexers/${var.search_indexer_name}/run?api-version=2024-07-01"
  }

  included_event_types = [
    "Microsoft.Storage.BlobCreated",
  ]

  subject_filter {
    subject_begins_with = "/blobServices/default/containers/${var.container_name}/"
  }

  advanced_filter {
    string_ends_with {
      key    = "subject"
      values = [".pdf"]
    }
  }
}

