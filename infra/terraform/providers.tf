terraform {
  required_version = ">= 1.14.7"

  required_providers {

    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 4.63.0"
    }

    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.4.0"
    }

    azapi = {
      source  = "azure/azapi"
      version = "~> 2.8.0"
    }

    kubernetes = {
      source  = "hashicorp/kubernetes"
      version = "~> 3.0.1"
    }

    kubectl = {
      source  = "gavinbunney/kubectl"
      version = "~> 1.19.0"
    }

    random = {
      source  = "hashicorp/random"
      version = "~> 3.6.0"
    }
  }

  backend "azurerm" {}
}

provider "azurerm" {
  features {
    key_vault {
      purge_soft_delete_on_destroy               = true
      recover_soft_deleted_key_vaults             = true
      purge_soft_deleted_certificates_on_destroy  = true
      recover_soft_deleted_certificates           = true
      purge_soft_deleted_secrets_on_destroy       = true
      recover_soft_deleted_secrets                = true
    }

    cognitive_account {
      purge_soft_delete_on_destroy = true
    }
  }

  resource_provider_registrations = "none"
}

provider "azapi" {
}

provider "azuread" {
}