# =============================================================================
# Idempotent Resource Imports
# Detect-and-import pattern: deploy scripts / CI workflows check whether
# resources already exist and pass their IDs as variables. Empty values
# mean "nothing to import — create fresh".
# =============================================================================

import {
  for_each = var.import_service_networking_id != "" ? toset([var.import_service_networking_id]) : toset([])
  to       = azurerm_resource_provider_registration.service_networking
  id       = each.value
}

import {
  for_each = var.existing_user_ids
  to       = module.entra.azuread_user.test[each.key]
  id       = each.value
}
