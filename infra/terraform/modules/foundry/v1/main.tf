# =============================================================================
# Azure OpenAI Module v1
# Creates: AI Services account + default Foundry project + model deployments
# =============================================================================

locals {
  account_name = "aif-${var.base_name}-${var.environment}-${var.location}"
  subdomain    = lower(local.account_name)
}

# -----------------------------------------------------------------------------
# AI Services Account
#
# `project_management_enabled = true` + a SystemAssigned identity + a custom
# subdomain are the three prereqs for `azurerm_cognitive_account_project`,
# which is the Terraform resource for the new (post-2025) flat Foundry project
# experience. Without these, the project resource fails creation and the
# `AIProjectClient` SDK has no project endpoint to bind to.
# -----------------------------------------------------------------------------
resource "azurerm_cognitive_account" "this" {
  name                       = local.account_name
  location                   = var.location
  resource_group_name        = var.resource_group_name
  kind                       = var.account_kind
  local_auth_enabled         = var.local_auth_enabled
  sku_name                   = var.sku_name
  custom_subdomain_name      = local.subdomain
  project_management_enabled = true
  tags                       = var.tags

  public_network_access_enabled = var.public_network_access_enabled

  identity {
    type = "SystemAssigned"
  }

  network_acls {
    default_action = var.network_default_action
    bypass         = "AzureServices"
    ip_rules       = var.allowed_ips
  }

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# Default Foundry Project
#
# Every "new Foundry experience" workflow (AIProjectClient, AgentAdministration,
# MemoryStores, project-scoped OpenAI quotas) requires a project resource under
# the AI Services account. The first project created on the account becomes the
# implicit default (exposed via the `default = true` computed attribute).
#
# The endpoint we care about for `AIProjectClient` is
# `endpoints["AI Foundry API"]` =
# https://<account>.services.ai.azure.com/api/projects/<project_name>
# -----------------------------------------------------------------------------
resource "azurerm_cognitive_account_project" "default" {
  name                 = var.project_name
  cognitive_account_id = azurerm_cognitive_account.this.id
  location             = var.location
  display_name         = var.project_display_name
  description          = var.project_description

  identity {
    type = "SystemAssigned"
  }

  tags = var.tags

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
    name     = var.deployment_sku_name
    capacity = var.deployment_capacity
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

