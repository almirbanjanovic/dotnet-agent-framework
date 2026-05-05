terraform {
  required_version = ">= 1.14"

  # Remote state in Azure Blob Storage. The backing storage account lives in
  # a SEPARATE, persistent resource group (`rg-dotnetagent-localdev-tfstate`)
  # so it survives `setup-local -Cleanup` (and even manual deletion of the
  # working `rg-dotnetagent-localdev`). Bootstrap and `backend.hcl` generation
  # are handled by `infra/setup-local.{ps1,sh}`.
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
