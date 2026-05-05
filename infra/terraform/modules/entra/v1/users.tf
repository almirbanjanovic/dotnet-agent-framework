# =============================================================================
# Entra Module v1 — Test Users
# Creates: Test users with random passwords and app role assignments
#
# Idempotent against pre-existing users: if a previous setup-local run was
# interrupted (or another developer in this tenant already provisioned the
# same UPNs), the data source below detects them and the `import` block
# brings them into terraform state instead of failing with
# `Request_BadRequest: Another object with the same value for property
# userPrincipalName already exists`.
# =============================================================================

# -----------------------------------------------------------------------------
# Detect pre-existing test users
# `ignore_missing = true` means a UPN that doesn't exist is silently dropped
# from the result set instead of failing the plan.
# -----------------------------------------------------------------------------

locals {
  desired_upns = {
    for key, user in var.test_users :
    key => "${user.mail_nickname}@${local.domain}"
  }
}

data "azuread_users" "existing" {
  user_principal_names = values(local.desired_upns)
  ignore_missing       = true
}

locals {
  # Lower-case UPN → object_id (Entra UPN comparisons are case-insensitive).
  existing_upn_to_oid = {
    for u in data.azuread_users.existing.users :
    lower(u.user_principal_name) => u.object_id
  }

  # user_key → object_id, only for keys whose UPN was found in the tenant.
  import_targets = {
    for key, upn in local.desired_upns :
    key => local.existing_upn_to_oid[lower(upn)]
    if contains(keys(local.existing_upn_to_oid), lower(upn))
  }
}

# -----------------------------------------------------------------------------
# Random Passwords (human-readable format: Contoso-<Pet>-<Number>!#)
# Generated for ALL users (terraform doesn't allow conditional resources),
# but only NEW users actually receive this password — for imported users,
# `lifecycle.ignore_changes = [password]` on `azuread_user.test` preserves
# whatever password the prior run set. The setup-local script consults
# `imported_user_keys` to decide which passwords to print.
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
# `import` block (Terraform 1.7+) brings any pre-existing UPNs into state
# before terraform plans changes. After import, terraform diffs the imported
# state against the desired config — `password` is excluded from drift
# detection so existing users keep their original password.
# -----------------------------------------------------------------------------

import {
  for_each = local.import_targets
  to       = azuread_user.test[each.key]
  id       = each.value
}

resource "azuread_user" "test" {
  for_each = var.test_users

  user_principal_name   = "${each.value.mail_nickname}@${local.domain}"
  display_name          = each.value.display_name
  mail_nickname         = each.value.mail_nickname
  password              = local.user_passwords[each.key]
  force_password_change = false
  account_enabled       = true

  # `password` is set on create only. Once imported, terraform will not
  # rotate it on subsequent plans — preserves the password from whichever
  # run originally provisioned the user.
  # `display_name` and `mail_nickname` are only set on create as well so
  # an imported user with manually-edited display name isn't overwritten.
  lifecycle {
    ignore_changes = [
      password,
      display_name,
      mail_nickname,
    ]
  }
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
