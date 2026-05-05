variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
}

variable "environment" {
  description = "Deployment environment (e.g., dev, staging, prod)"
  type        = string
}

variable "redirect_uris" {
  description = "Redirect URIs for the SPA app registration (MSAL PKCE callback)"
  type        = list(string)
  # Default matches the Blazor UI port the AppHost assigns in src/AppHost/Program.cs
  # (HTTP, port 5008). MSAL Browser allows http:// only for localhost.
  default = ["http://localhost:5008/authentication/login-callback"]
}

variable "mail_nickname_suffix" {
  description = "Suffix appended to every test user's mail_nickname (and therefore UPN) to keep test users from colliding with another deployment of this module in the same tenant. Tenants enforce UPN uniqueness, so the Local Track and Full Azure Track CANNOT both create `emma.wilson@<tenant>` — give one of them a suffix like `-local`. Empty by default."
  type        = string
  default     = ""
}

variable "test_users" {
  description = "Map of customer test users to create in Entra ID. Each maps to a pre-seeded customer in Cosmos DB / the in-memory CSV."
  type = map(object({
    display_name  = string
    mail_nickname = string
    customer_id   = string
    roles         = list(string)
  }))
  default = {
    emma = {
      display_name  = "Emma Wilson"
      mail_nickname = "emma.wilson"
      customer_id   = "101"
      roles         = ["Customer"]
    }
    james = {
      display_name  = "James Chen"
      mail_nickname = "james.chen"
      customer_id   = "102"
      roles         = ["Customer"]
    }
    sarah = {
      display_name  = "Sarah Miller"
      mail_nickname = "sarah.miller"
      customer_id   = "103"
      roles         = ["Customer"]
    }
    david = {
      display_name  = "David Park"
      mail_nickname = "david.park"
      customer_id   = "104"
      roles         = ["Customer"]
    }
    lisa = {
      display_name  = "Lisa Torres"
      mail_nickname = "lisa.torres"
      customer_id   = "105"
      roles         = ["Customer"]
    }
    mike = {
      display_name  = "Mike Johnson"
      mail_nickname = "mike.johnson"
      customer_id   = "106"
      roles         = ["Customer"]
    }
    anna = {
      display_name  = "Anna Roberts"
      mail_nickname = "anna.roberts"
      customer_id   = "107"
      roles         = ["Customer"]
    }
    tom = {
      display_name  = "Tom Garcia"
      mail_nickname = "tom.garcia"
      customer_id   = "108"
      roles         = ["Customer"]
    }
  }
}
