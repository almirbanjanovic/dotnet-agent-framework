#--------------------------------------------------------------------------------------------------------------------------------
# Post-Apply Health Checks
# These check blocks run after apply and emit warnings (not errors)
# if endpoints are unreachable. They do not block terraform apply.
#--------------------------------------------------------------------------------------------------------------------------------

check "aks_oidc_reachable" {
  data "http" "aks_oidc" {
    url = module.aks.oidc_issuer_url
  }

  assert {
    condition     = data.http.aks_oidc.status_code == 200
    error_message = "AKS OIDC issuer is not reachable"
  }
}

check "keyvault_reachable" {
  data "http" "keyvault" {
    url = module.keyvault.vault_uri
  }

  assert {
    condition     = data.http.keyvault.status_code < 500
    error_message = "Key Vault endpoint is not reachable"
  }
}

check "search_reachable" {
  data "http" "search" {
    url = module.search.endpoint
  }

  assert {
    condition     = data.http.search.status_code < 500
    error_message = "AI Search endpoint is not reachable"
  }
}
