# =============================================================================
# Entra Module v1 — Test Users
# Creates: Test users with random passwords and app role assignments
#
# Idempotency: terraform owns these users completely. The setup-local script
# deletes any orphan UPNs in Entra (left from a prior failed/destroyed run)
# BEFORE running `terraform apply` — but ONLY if the UPN is missing from
# Terraform state. Users that are already managed by TF are left untouched,
# so repeat `setup-local` runs are a no-op for them: no recreate, no
# password rotation, no invalidated browser sessions.
#
# We deliberately do NOT use a Terraform `import` block: that path failed
# in practice because `data.azuread_users` returned empty results in some
# tenants AND because `import` blocks driven by an `auto.tfvars` variable
# still produced "will be created" plans without honoring the import.
# Selective delete-then-create (genuine orphans only) is simpler,
# deterministic, and Terraform-only.
# =============================================================================

locals {
  # Effective mail_nickname (= UPN local-part) per user. The suffix lets the
  # Local Track and Full Azure Track coexist in the same tenant without UPN
  # collisions — caller passes `mail_nickname_suffix = "-local"` for one of
  # them. Empty suffix = vanilla nicknames.
  effective_nicknames = {
    for key, user in var.test_users :
    key => "${user.mail_nickname}${var.mail_nickname_suffix}"
  }

  desired_upns = {
    for key, nick in local.effective_nicknames :
    key => "${nick}@${local.domain}"
  }
}

# -----------------------------------------------------------------------------
# Random Passwords (human-readable format: Contoso-<Pet>-<Number>!#)
# These resources have no `keepers`, so they stay stable across applies —
# the password only changes when the random_pet / random_integer entry is
# removed from state (i.e. after a `terraform destroy`).
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
# -----------------------------------------------------------------------------

resource "azuread_user" "test" {
  for_each = var.test_users

  user_principal_name   = local.desired_upns[each.key]
  display_name          = each.value.display_name
  mail_nickname         = local.effective_nicknames[each.key]
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
