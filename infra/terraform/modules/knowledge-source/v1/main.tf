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
          }
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
    environment = {
      SEARCH_ENDPOINT = var.search_endpoint
      SEARCH_API_KEY  = nonsensitive(var.search_api_key)
      BODY            = local.body
    }
    command = <<-EOT
      $h = @{ 'api-key' = $env:SEARCH_API_KEY; 'Content-Type' = 'application/json' }
      Invoke-RestMethod -Uri "$($env:SEARCH_ENDPOINT)/knowledgesources/${var.name}?api-version=2025-11-01-preview" `
        -Method Put -Headers $h -Body ([System.Text.Encoding]::UTF8.GetBytes($env:BODY))
    EOT
  }
}
