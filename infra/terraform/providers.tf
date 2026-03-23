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
#
# Authentication:
# - Local: deploy script creates a temp client secret and passes it via
#   TF_VAR_msgraph_client_id/secret. Only msgraph uses it; other providers
#   keep using CLI auth.
# - CI/CD: ARM_USE_OIDC=true → all providers (including msgraph) use OIDC.
#   The msgraph_client_* vars are left empty and use_oidc takes over.
provider "msgraph" {
  use_cli       = false
  use_oidc      = var.msgraph_client_secret == "" ? true : false
  client_id     = var.msgraph_client_id != "" ? var.msgraph_client_id : null
  client_secret = var.msgraph_client_secret != "" ? var.msgraph_client_secret : null
  tenant_id     = var.msgraph_tenant_id != "" ? var.msgraph_tenant_id : null
}

# kubectl provider — configured dynamically from AKS cluster credentials.
#
# var.deploy_k8s_resources gates BOTH the provider config and all kubectl_manifest
# resources. When false, the provider gets empty credentials (no connection attempt)
# and all kubectl resources are skipped via count/for_each conditions.
#
# The deploy scripts detect AKS reachability (az aks show + DNS resolution) and
# automatically set deploy_k8s_resources=false for the first pass when AKS is
# unreachable, then run a second pass with deploy_k8s_resources=true after AKS
# is created. See deploy.ps1/deploy.sh "Pre-plan: kubectl state guard".
provider "kubectl" {
  host                   = var.deploy_k8s_resources ? try(module.aks.kube_config_host, "") : ""
  client_certificate     = var.deploy_k8s_resources ? try(base64decode(module.aks.kube_config_client_certificate), "") : ""
  client_key             = var.deploy_k8s_resources ? try(base64decode(module.aks.kube_config_client_key), "") : ""
  cluster_ca_certificate = var.deploy_k8s_resources ? try(base64decode(module.aks.kube_config_cluster_ca), "") : ""
  load_config_file       = false
}
