# =============================================================================
# Azure AI Search Module v1
# Creates: Search Service (azurerm) + Index, Data Source, Skillset, Indexer (azapi)
# =============================================================================

locals {
  search_service_name = "srch-${var.environment}"
}

# -----------------------------------------------------------------------------
# AI Search Service (control plane)
# -----------------------------------------------------------------------------
resource "azurerm_search_service" "this" {
  name                = local.search_service_name
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = var.sku

  identity {
    type = "SystemAssigned"
  }

  tags = var.tags

  lifecycle {
    ignore_changes = [tags]
  }
}

# -----------------------------------------------------------------------------
# Search Index (data plane via AzAPI)
# -----------------------------------------------------------------------------
resource "azapi_resource" "search_index" {
  type      = "Microsoft.Search/searchServices/indexes@2024-07-01"
  name      = var.index_name
  parent_id = azurerm_search_service.this.id

  body = {
    properties = {
      fields = [
        {
          name       = "id"
          type       = "Edm.String"
          key        = true
          filterable = true
          sortable   = false
          facetable  = false
          searchable = false
        },
        {
          name       = "title"
          type       = "Edm.String"
          key        = false
          filterable = true
          sortable   = true
          facetable  = false
          searchable = true
        },
        {
          name       = "category"
          type       = "Edm.String"
          key        = false
          filterable = true
          sortable   = false
          facetable  = true
          searchable = false
        },
        {
          name       = "source_file"
          type       = "Edm.String"
          key        = false
          filterable = true
          sortable   = false
          facetable  = false
          searchable = false
        },
        {
          name       = "chunk_index"
          type       = "Edm.Int32"
          key        = false
          filterable = false
          sortable   = true
          facetable  = false
          searchable = false
        },
        {
          name       = "content"
          type       = "Edm.String"
          key        = false
          filterable = false
          sortable   = false
          facetable  = false
          searchable = true
        },
        {
          name                 = "content_vector"
          type                 = "Collection(Edm.Single)"
          key                  = false
          filterable           = false
          sortable             = false
          facetable            = false
          searchable           = true
          vectorSearchProfile = "vector-profile"
        }
      ]

      vectorSearch = {
        algorithms = [
          {
            name = "hnsw-algorithm"
            kind = "hnsw"
            hnswParameters = {
              metric = "cosine"
              m      = 4
              efConstruction = 400
              efSearch       = 500
            }
          }
        ]
        profiles = [
          {
            name          = "vector-profile"
            algorithmConfigurationName = "hnsw-algorithm"
            vectorizer    = "openai-vectorizer"
          }
        ]
        vectorizers = [
          {
            name = "openai-vectorizer"
            kind = "azureOpenAI"
            azureOpenAIParameters = {
              resourceUri  = var.openai_endpoint
              deploymentId = var.openai_embedding_deployment
              modelName    = var.openai_embedding_model
            }
          }
        ]
      }
    }
  }

  schema_validation_enabled = false
}

# -----------------------------------------------------------------------------
# Data Source — Blob Storage (data plane via AzAPI)
# -----------------------------------------------------------------------------
resource "azapi_resource" "search_data_source" {
  type      = "Microsoft.Search/searchServices/dataSources@2024-07-01"
  name      = "blob-sharepoint-docs"
  parent_id = azurerm_search_service.this.id

  body = {
    properties = {
      type = "azureblob"
      credentials = {
        connectionString = "ResourceId=${var.storage_account_id};"
      }
      container = {
        name = var.container_name
      }
    }
  }

  schema_validation_enabled = false
}

# -----------------------------------------------------------------------------
# Skillset — Text Split + Azure OpenAI Embedding (data plane via AzAPI)
# -----------------------------------------------------------------------------
resource "azapi_resource" "search_skillset" {
  type      = "Microsoft.Search/searchServices/skillsets@2024-07-01"
  name      = "vectorize-skillset"
  parent_id = azurerm_search_service.this.id

  body = {
    properties = {
      skills = [
        {
          "@odata.type" = "#Microsoft.Skills.Text.SplitSkill"
          name          = "split-text"
          description   = "Split document text into chunks"
          context       = "/document"
          inputs = [
            {
              name   = "text"
              source = "/document/content"
            }
          ]
          outputs = [
            {
              name       = "textItems"
              targetName = "pages"
            }
          ]
          textSplitMode    = "pages"
          maximumPageLength = 2000
          pageOverlapLength = 200
        },
        {
          "@odata.type" = "#Microsoft.Skills.Text.AzureOpenAIEmbeddingSkill"
          name          = "embed-text"
          description   = "Generate vector embeddings for each chunk"
          context       = "/document/pages/*"
          inputs = [
            {
              name   = "text"
              source = "/document/pages/*"
            }
          ]
          outputs = [
            {
              name       = "embedding"
              targetName = "vector"
            }
          ]
          resourceUri  = var.openai_endpoint
          deploymentId = var.openai_embedding_deployment
          modelName    = var.openai_embedding_model
        }
      ]
      indexProjections = {
        selectors = [
          {
            targetIndexName    = var.index_name
            parentKeyFieldName = "parent_id"
            sourceContext       = "/document/pages/*"
            mappings = [
              {
                name   = "content"
                source = "/document/pages/*"
              },
              {
                name   = "content_vector"
                source = "/document/pages/*/vector"
              },
              {
                name   = "title"
                source = "/document/metadata_storage_name"
              },
              {
                name   = "source_file"
                source = "/document/metadata_storage_path"
              }
            ]
          }
        ]
        parameters = {
          projectionMode = "generatedKeyAsId"
        }
      }
    }
  }

  schema_validation_enabled = false

  depends_on = [azapi_resource.search_index]
}

# -----------------------------------------------------------------------------
# Indexer — Blob → Skillset → Index (data plane via AzAPI)
# -----------------------------------------------------------------------------
resource "azapi_resource" "search_indexer" {
  type      = "Microsoft.Search/searchServices/indexers@2024-07-01"
  name      = "blob-indexer"
  parent_id = azurerm_search_service.this.id

  body = {
    properties = {
      dataSourceName  = azapi_resource.search_data_source.name
      targetIndexName = var.index_name
      skillsetName    = azapi_resource.search_skillset.name
      schedule = {
        interval = "PT5M"
      }
      parameters = {
        configuration = {
          dataToExtract       = "contentAndMetadata"
          parsingMode          = "default"
          imageAction          = "none"
        }
      }
      fieldMappings = [
        {
          sourceFieldName = "metadata_storage_path"
          targetFieldName = "source_file"
        }
      ]
    }
  }

  schema_validation_enabled = false

  depends_on = [
    azapi_resource.search_index,
    azapi_resource.search_data_source,
    azapi_resource.search_skillset,
  ]
}
