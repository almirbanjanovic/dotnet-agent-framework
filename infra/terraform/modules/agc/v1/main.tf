# =============================================================================
# App Gateway for Containers (AGC) Module v1
# Creates: Application Load Balancer + Frontend + Subnet Association
# Requires: ALB Controller extension on AKS (configured in AKS module)
# =============================================================================

resource "azurerm_application_load_balancer" "this" {
  name                = "agc-${var.base_name}-${var.environment}-${var.location}"
  location            = var.location
  resource_group_name = var.resource_group_name
  tags                = var.tags
}

resource "azurerm_application_load_balancer_frontend" "this" {
  name                         = "agc-frontend-${var.environment}"
  application_load_balancer_id = azurerm_application_load_balancer.this.id
}

resource "azurerm_application_load_balancer_subnet_association" "this" {
  name                         = "agc-subnet-assoc-${var.environment}"
  application_load_balancer_id = azurerm_application_load_balancer.this.id
  subnet_id                    = var.subnet_id
}
