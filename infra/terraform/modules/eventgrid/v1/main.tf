# =============================================================================
# Event Grid Module v1
# Creates: System Topic + Logic App + Event Subscription
#
# Architecture: Event Grid (BlobCreated) → Logic App → AI Search indexer run API
#
# Why a Logic App intermediary?
# Event Grid webhooks cannot send custom headers. The AI Search indexer REST API
# requires an api-key header for authentication. A Logic App bridges the gap:
# - Its HTTP Request trigger handles Event Grid's validation handshake automatically
# - Its callback URL includes a SAS token (no additional auth needed for Event Grid)
# - Its HTTP action calls the Search indexer with the required api-key header
# =============================================================================

locals {
  system_topic_name = "evgt-${var.base_name}-${var.environment}-${var.location}"
  logic_app_name    = "logic-${var.base_name}-${var.environment}-indexer"
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
# Logic App — Bridges Event Grid → AI Search indexer (handles auth + validation)
# -----------------------------------------------------------------------------
resource "azurerm_logic_app_workflow" "indexer_trigger" {
  name                = local.logic_app_name
  location            = var.location
  resource_group_name = var.resource_group_name

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

resource "azurerm_logic_app_trigger_http_request" "event_grid" {
  name         = "When_a_blob_is_uploaded"
  logic_app_id = azurerm_logic_app_workflow.indexer_trigger.id

  schema = jsonencode({
    type = "object"
    properties = {
      topic       = { type = "string" }
      subject     = { type = "string" }
      eventType   = { type = "string" }
      eventTime   = { type = "string" }
      id          = { type = "string" }
      dataVersion = { type = "string" }
      data = {
        type       = "object"
        properties = {}
      }
    }
  })
}

resource "azurerm_logic_app_action_http" "run_indexer" {
  name         = "Run_Search_Indexer"
  logic_app_id = azurerm_logic_app_workflow.indexer_trigger.id
  method       = "POST"
  uri          = "https://${var.search_service_name}.search.windows.net/indexers/${var.search_indexer_name}/run?api-version=2024-11-01"

  headers = {
    "api-key"      = var.search_api_key
    "Content-Type" = "application/json"
  }
}

# -----------------------------------------------------------------------------
# Event Subscription — BlobCreated → Logic App
# Filters to .pdf files in the target container only.
# -----------------------------------------------------------------------------
resource "azurerm_eventgrid_system_topic_event_subscription" "blob_created" {
  name                = "blob-created-to-indexer"
  system_topic        = azurerm_eventgrid_system_topic.this.name
  resource_group_name = var.resource_group_name

  webhook_endpoint {
    url = azurerm_logic_app_trigger_http_request.event_grid.callback_url
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

