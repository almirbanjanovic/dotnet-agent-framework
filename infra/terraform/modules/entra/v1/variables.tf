variable "base_name" {
  description = "Project base name for resource naming"
  type        = string
}

variable "environment" {
  description = "Deployment environment (e.g., dev, staging, prod)"
  type        = string
}

variable "redirect_uris" {
  description = "Redirect URIs for the BFF app registration (OpenID Connect callback)"
  type        = list(string)
  default     = ["https://localhost:5001/signin-oidc"]
}

variable "test_users" {
  description = "Map of test users to create in Entra ID. Each user gets a random password stored in Key Vault."
  type = map(object({
    display_name  = string
    mail_nickname = string
    roles         = list(string)
  }))
  default = {
    emma = {
      display_name  = "Emma CS Rep"
      mail_nickname = "emma.csrep"
      roles         = ["Agent.User"]
    }
    bob = {
      display_name  = "Bob Senior Rep"
      mail_nickname = "bob.senior"
      roles         = ["Agent.User", "Data.Writer"]
    }
    sarah = {
      display_name  = "Sarah Manager"
      mail_nickname = "sarah.manager"
      roles         = ["Agent.User", "Data.Writer"]
    }
    dave = {
      display_name  = "Dave Readonly"
      mail_nickname = "dave.readonly"
      roles         = []
    }
    admin = {
      display_name  = "Admin Contoso"
      mail_nickname = "admin.contoso"
      roles         = ["Agent.User", "Data.Writer"]
    }
  }
}
