terraform {
  required_version = ">= 1.14"

  backend "local" {}

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.63"
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
