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

    msgraph = {
      source  = "microsoft/msgraph"
      version = "~> 0.3.0"
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
      recover_soft_deleted_key_vaults            = true
      purge_soft_deleted_certificates_on_destroy = true
      recover_soft_deleted_certificates          = true
      purge_soft_deleted_secrets_on_destroy      = true
      recover_soft_deleted_secrets               = true
      purge_soft_deleted_keys_on_destroy         = true
      recover_soft_deleted_keys                  = true
    }

    cognitive_account {
      purge_soft_delete_on_destroy = true
    }

    api_management {
      purge_soft_delete_on_destroy = true
      recover_soft_deleted         = true
    }

    resource_group {
      prevent_deletion_if_contains_resources = false
    }
  }

  storage_use_azuread = true

  resource_provider_registrations = "extended"
}

# Register resource providers that may not be registered by default
resource "azurerm_resource_provider_registration" "service_networking" {
  name = "Microsoft.ServiceNetworking"
}

provider "azapi" {
}

provider "azuread" {
}

# msgraph provider: use_cli = false avoids the Azure CLI's delegated token
# which includes Directory.AccessAsUser.All (a first-party Microsoft permission
# on the Azure CLI app). The Agent ID API explicitly blocks that permission.
# Instead, authenticate via client credentials (service principal) or OIDC.
provider "msgraph" {
  use_cli = false
}

# kubectl provider is configured dynamically using AKS cluster credentials.
# The kubernetes provider is declared but not configured — only kubectl is used
# because it handles unknown values at plan time (when AKS doesn't exist yet).
provider "kubectl" {
  host                   = module.aks.kube_config_host
  client_certificate     = base64decode(module.aks.kube_config_client_certificate)
  client_key             = base64decode(module.aks.kube_config_client_key)
  cluster_ca_certificate = base64decode(module.aks.kube_config_cluster_ca)
  load_config_file       = false
}
