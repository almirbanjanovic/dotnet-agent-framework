#--------------------------------------------------------------------------------------------------------------------------------
# Diagnostic Settings — Audit logs to Log Analytics
# Sends Key Vault audit events and Cosmos DB control/data plane logs to the
# Log Analytics workspace provisioned by the AKS module.
#--------------------------------------------------------------------------------------------------------------------------------

resource "azurerm_monitor_diagnostic_setting" "keyvault" {
  name                       = "diag-keyvault-to-law"
  target_resource_id         = module.keyvault.id
  log_analytics_workspace_id = module.aks.log_analytics_workspace_id

  enabled_log {
    category = "AuditEvent"
  }

  metric {
    category = "AllMetrics"
    enabled  = false
  }
}

resource "azurerm_monitor_diagnostic_setting" "cosmosdb_crm" {
  name                       = "diag-cosmos-crm-to-law"
  target_resource_id         = module.cosmosdb_crm.account_id
  log_analytics_workspace_id = module.aks.log_analytics_workspace_id

  enabled_log {
    category = "DataPlaneRequests"
  }

  enabled_log {
    category = "ControlPlaneRequests"
  }

  metric {
    category = "AllMetrics"
    enabled  = false
  }
}

resource "azurerm_monitor_diagnostic_setting" "cosmosdb_agents" {
  name                       = "diag-cosmos-agents-to-law"
  target_resource_id         = module.cosmosdb_agents.account_id
  log_analytics_workspace_id = module.aks.log_analytics_workspace_id

  enabled_log {
    category = "DataPlaneRequests"
  }

  enabled_log {
    category = "ControlPlaneRequests"
  }

  metric {
    category = "AllMetrics"
    enabled  = false
  }
}
