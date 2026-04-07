# =============================================================================
# Knowledge Source Module v1
# Creates a Knowledge Source via the Search data-plane REST API (2025-11-01-preview)
#
# A single PUT auto-generates: index, data source, skillset, indexer.
# Must be created AFTER RBAC + blob uploads so the indexer can access resources.
# =============================================================================

locals {
  body = jsonencode({
    name = var.name
    kind = "azureBlob"
    azureBlobParameters = {
      connectionString = "ResourceId=${var.storage_account_id};"
      containerName    = var.container_name
      ingestionParameters = {
        disableImageVerbalization = true
        contentExtractionMode     = "minimal"
        ingestionSchedule = {
          interval = "PT5M"
        }
        embeddingModel = {
          kind = "azureOpenAI"
          azureOpenAIParameters = {
            resourceUri  = var.openai_endpoint
            deploymentId = var.openai_embedding_deployment
            modelName    = var.openai_embedding_model
            # apiKey and authIdentity intentionally omitted —
            # per Azure docs, this triggers system-assigned managed identity.
          }
        }
        # Provide AI Services endpoint WITHOUT apiKey so the auto-generated
        # skillset uses managed identity (AIServicesByIdentity) instead of
        # trying to discover/use a disabled API key (local_auth_enabled=false).
        aiServices = {
          uri = var.openai_endpoint
        }
      }
    }
  })
}

resource "terraform_data" "this" {
  triggers_replace = {
    body = local.body
  }

  provisioner "local-exec" {
    interpreter = ["pwsh", "-Command"]
    # SECURITY NOTE: nonsensitive() is required because Terraform does not support
    # passing sensitive values into provisioner environment blocks. The API key is
    # used only at Terraform provisioning time (not stored for runtime use) and is
    # passed via environment variable (not command-line argument) to minimize exposure.
    environment = {
      SEARCH_ENDPOINT = var.search_endpoint
      SEARCH_API_KEY  = nonsensitive(var.search_api_key)
      BODY            = local.body
    }
    command = <<-EOT
      $h = @{ 'api-key' = $env:SEARCH_API_KEY; 'Content-Type' = 'application/json' }
      $uri = "$($env:SEARCH_ENDPOINT)/knowledgesources/${var.name}?api-version=2025-11-01-preview"
      try {
        Invoke-RestMethod -Uri $uri -Method Put -Headers $h -Body ([System.Text.Encoding]::UTF8.GetBytes($env:BODY))
        Write-Host "Knowledge source '${var.name}' created successfully."
      } catch {
        $err = $_.ErrorDetails.Message | ConvertFrom-Json -ErrorAction SilentlyContinue
        if ($err.error.code -eq 'InvalidRequestParameter' -and $err.error.message -match 'embedding model') {
          Write-Host "Knowledge source '${var.name}' exists with immutable config. Deleting and recreating..."
          Invoke-RestMethod -Uri $uri -Method Delete -Headers @{ 'api-key' = $env:SEARCH_API_KEY } | Out-Null
          Start-Sleep -Seconds 5
          Invoke-RestMethod -Uri $uri -Method Put -Headers $h -Body ([System.Text.Encoding]::UTF8.GetBytes($env:BODY))
          Write-Host "Knowledge source '${var.name}' recreated successfully."
        } else {
          throw
        }
      }
    EOT
  }
}
