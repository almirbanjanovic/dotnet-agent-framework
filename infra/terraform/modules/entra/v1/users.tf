# =============================================================================
# Entra Module v1 — Test Users
# Creates: Test users with random passwords and app role assignments
# =============================================================================

# -----------------------------------------------------------------------------
# Random Passwords (human-readable format: Contoso-<Pet>-<Number>!#)
# -----------------------------------------------------------------------------

resource "random_pet" "user_password_pet" {
  for_each  = var.test_users
  length    = 1
  separator = ""
}

resource "random_integer" "user_password_num" {
  for_each = var.test_users
  min      = 1000
  max      = 9999
}

locals {
  user_passwords = {
    for key, user in var.test_users : key => "Contoso-${title(random_pet.user_password_pet[key].id)}-${random_integer.user_password_num[key].result}!#"
  }
}

# -----------------------------------------------------------------------------
# Test Users
# Uses lifecycle prevent_destroy = false and import for existing users.
# If users already exist from a previous run, import them into state:
#   terraform import 'module.entra.azuread_user.test[\"emma\"]' <object-id>
# NOTE: Once real object IDs are known, convert these to declarative
#       import blocks (Terraform 1.5+) instead of CLI import commands.
# -----------------------------------------------------------------------------

resource "azuread_user" "test" {
  for_each = var.test_users

  user_principal_name   = "${each.value.mail_nickname}@${local.domain}"
  display_name          = each.value.display_name
  mail_nickname         = each.value.mail_nickname
  password              = local.user_passwords[each.key]
  force_password_change = false
  account_enabled       = true
}

# -----------------------------------------------------------------------------
# Role Assignments
# Flatten the user→roles map into individual assignments
# -----------------------------------------------------------------------------

locals {
  # Build a flat list: [{user_key, role_value}, ...]
  role_assignments = flatten([
    for user_key, user in var.test_users : [
      for role in user.roles : {
        key      = "${user_key}-${role}"
        user_key = user_key
        role     = role
      }
    ]
  ])

  # Map role value → role ID from the app registration
  role_id_map = {
    "Customer" = random_uuid.role_customer.result
  }
}

resource "azuread_app_role_assignment" "test_users" {
  for_each = { for ra in local.role_assignments : ra.key => ra }

  app_role_id         = local.role_id_map[each.value.role]
  principal_object_id = azuread_user.test[each.value.user_key].object_id
  resource_object_id  = azuread_service_principal.bff.object_id
}
