# =============================================================================
# Private DNS Zones Module v1
# Creates: Private DNS Zones + VNet Links (one per zone)
# =============================================================================

resource "azurerm_private_dns_zone" "this" {
  for_each = var.zones

  name                = each.value
  resource_group_name = var.resource_group_name
  tags                = var.tags

  lifecycle {
    ignore_changes        = [tags]
    create_before_destroy = true
  }
}

resource "azurerm_private_dns_zone_virtual_network_link" "this" {
  for_each = var.zones

  name                  = "${each.key}-vnet-link"
  resource_group_name   = var.resource_group_name
  private_dns_zone_name = azurerm_private_dns_zone.this[each.key].name
  virtual_network_id    = var.vnet_id
  registration_enabled  = false
  tags                  = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}
