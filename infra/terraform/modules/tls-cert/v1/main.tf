# =============================================================================
# TLS Certificate Module v1
# Creates: Self-signed certificate in Key Vault for AGC TLS termination
# =============================================================================

resource "azurerm_key_vault_certificate" "tls" {
  name         = var.cert_name
  key_vault_id = var.key_vault_id

  certificate_policy {
    issuer_parameters {
      name = "Self"
    }

    key_properties {
      exportable = true
      key_size   = 2048
      key_type   = "RSA"
      reuse_key  = true
    }

    secret_properties {
      content_type = "application/x-pkcs12"
    }

    lifetime_action {
      action {
        action_type = "AutoRenew"
      }
      trigger {
        days_before_expiry = 30
      }
    }

    x509_certificate_properties {
      subject            = "CN=${var.common_name}"
      validity_in_months = 12

      subject_alternative_names {
        dns_names = var.dns_names
      }

      key_usage = [
        "digitalSignature",
        "keyEncipherment",
      ]

      extended_key_usage = ["1.3.6.1.5.5.7.3.1"] # serverAuth
    }
  }

  lifecycle {
    create_before_destroy = true
  }
}
