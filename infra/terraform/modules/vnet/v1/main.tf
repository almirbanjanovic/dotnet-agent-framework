# =============================================================================
# VNet Module v1
# Creates: Virtual Network with subnets for AKS (system + workload) and AGC
# =============================================================================

resource "azurerm_virtual_network" "this" {
  name                = "vnet-${var.base_name}-${var.environment}-${var.location}"
  location            = var.location
  resource_group_name = var.resource_group_name
  address_space       = [var.address_space]
  tags                = var.tags
}

# -----------------------------------------------------------------------------
# Subnets
# -----------------------------------------------------------------------------

resource "azurerm_subnet" "aks_system" {
  name                 = "snet-aks-system"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.aks_system_subnet_cidr]
}

resource "azurerm_subnet" "aks_workload" {
  name                 = "snet-aks-workload"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.aks_workload_subnet_cidr]
}

resource "azurerm_subnet" "agc" {
  name                 = "snet-agc"
  resource_group_name  = var.resource_group_name
  virtual_network_name = azurerm_virtual_network.this.name
  address_prefixes     = [var.agc_subnet_cidr]

  delegation {
    name = "agc-delegation"
    service_delegation {
      name    = "Microsoft.ServiceNetworking/trafficControllers"
      actions = ["Microsoft.Network/virtualNetworks/subnets/join/action"]
    }
  }
}
