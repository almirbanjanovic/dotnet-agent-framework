output "system_topic_id" {
  description = "Event Grid System Topic resource ID"
  value       = azurerm_eventgrid_system_topic.this.id
}

output "logic_app_name" {
  description = "Logic App workflow name"
  value       = azurerm_logic_app_workflow.indexer_trigger.name
}

output "subscription_id" {
  description = "Event Grid Event Subscription resource ID"
  value       = azurerm_eventgrid_system_topic_event_subscription.blob_created.id
}
