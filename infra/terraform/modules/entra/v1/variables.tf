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
  default     = ["https://localhost:5002/authentication/login-callback"]
}

variable "test_users" {
  description = "Map of customer test users to create in Entra ID. Each maps to a pre-seeded customer in Azure SQL."
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
  }
}
