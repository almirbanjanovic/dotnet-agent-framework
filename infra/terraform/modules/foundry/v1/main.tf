# =============================================================================
# Azure OpenAI Module v1
# Creates: AI Services account + model deployment
# =============================================================================

locals {
  account_name = "aif-${var.base_name}-${var.environment}-${var.location}"
  subdomain    = lower(local.account_name)
}

# -----------------------------------------------------------------------------
# AI Services Account
# -----------------------------------------------------------------------------
resource "azurerm_cognitive_account" "this" {
  name                  = local.account_name
  location              = var.location
  resource_group_name   = var.resource_group_name
  kind                  = var.account_kind
  local_auth_enabled    = false
  sku_name              = var.sku_name
  custom_subdomain_name = local.subdomain
  tags                  = var.tags

  public_network_access_enabled = var.public_network_access_enabled

  network_acls {
    default_action = "Deny"
    ip_rules       = var.allowed_ips
  }

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# Chat Model Deployment
# -----------------------------------------------------------------------------
resource "azurerm_cognitive_deployment" "this" {
  name                 = var.deployment_model_name
  cognitive_account_id = azurerm_cognitive_account.this.id

  model {
    format  = var.deployment_model_format
    name    = var.deployment_model_name
    version = var.deployment_model_version
  }

  sku {
    name = var.deployment_sku_name
  }

  version_upgrade_option = var.version_upgrade_option
}

# -----------------------------------------------------------------------------
# Embedding Model Deployment
# -----------------------------------------------------------------------------
resource "azurerm_cognitive_deployment" "embedding" {
  count                = var.create_embedding_deployment ? 1 : 0
  name                 = var.embedding_model_name
  cognitive_account_id = azurerm_cognitive_account.this.id

  model {
    format  = var.deployment_model_format
    name    = var.embedding_model_name
    version = var.embedding_model_version
  }

  sku {
    name     = var.embedding_sku_name
    capacity = var.embedding_capacity
  }

  version_upgrade_option = var.version_upgrade_option

  depends_on = [azurerm_cognitive_deployment.this]
}

