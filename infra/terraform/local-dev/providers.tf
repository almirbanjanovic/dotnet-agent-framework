terraform {
  required_version = ">= 1.14"

  # Remote state in Azure Blob Storage, co-located with the rest of the
  # stack inside the working RG `rg-dotnetagent-localdev`. The state
  # storage account is bootstrapped out-of-band by
  # `infra/setup-local.{ps1,sh}` (Azure CLI), so it's not in TF state and
  # `terraform destroy` won't touch it — state survives both `-Cleanup`
  # and a full re-apply.
  backend "azurerm" {}

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.63"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    # Used to look up the deployer's public IP at apply time so we can
    # punch a hole in the Foundry account firewall for this developer.
    http = {
      source  = "hashicorp/http"
      version = "~> 3.4"
    }
  }
}

provider "azurerm" {
  features {
    cognitive_account {
      purge_soft_delete_on_destroy = true
    }
    resource_group {
      prevent_deletion_if_contains_resources = false
    }
  }
}

provider "azuread" {}
