output "system_topic_id" {
  description = "Event Grid System Topic resource ID"
  value       = azurerm_eventgrid_system_topic.this.id
}

output "subscription_id" {
  description = "Event Grid Event Subscription resource ID"
  value       = azurerm_eventgrid_event_subscription.blob_created.id
}
