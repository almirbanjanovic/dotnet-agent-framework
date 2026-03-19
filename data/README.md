# Data

This folder contains simulated enterprise data sources for the Contoso Outdoors agent framework. The data represents what you'd find in a real company's systems — a product/order database, a document library, and a product image store.

> **This data is provisioned automatically during `terraform apply`.** Product images and SharePoint PDFs are uploaded to Azure Blob Storage. CRM data is seeded into Cosmos DB via the seed tool (`src/seed-data/`). SharePoint PDFs are indexed by Azure AI Search via integrated vectorization — no manual vectorization step needed.

## What is RAG?

**RAG (Retrieval-Augmented Generation)** is the pattern that connects your company's data to a large language model. Without RAG, the chat model only knows what it was trained on. With RAG, it can answer questions about *your* specific policies, customers, and data — without fine-tuning the model.

The key insight: you can't search unstructured text (like a PDF policy document) with a database query. If a customer asks *"what is your return policy?"*, a keyword search for those exact words would return nothing — because the document is titled "Return and Refund Policy" and uses different phrasing.

**Vector embeddings solve this.** An embedding model converts text into numerical vectors that capture *meaning*, not exact words. Two texts that are semantically similar produce vectors that are close together — even if they share no words. This enables semantic search: find documents by meaning, not by keyword match.

### The two models

| Model | Role | Used when |
|-------|------|-----------|
| **Embedding model** (text-embedding-ada-002) | Converts text → 1536-dim vector | Indexing (via AI Search skillset) and query time (via AI Search integrated vectorization) |
| **Chat model** (gpt-4.1) | Generates natural language answers | Query time — receives user question + retrieved documents as context |

### The RAG flow

```
User: "What is your return policy?"
                    │
                    ▼
    ┌─────────────────────────────┐
    │  1. EMBED the question      │
    │  embedding model → vector   │
    └──────────────┬──────────────┘
                   │
                   ▼
    ┌─────────────────────────────┐
    │  2. RETRIEVE relevant docs  │
    │  Azure AI Search            │
    │  (hybrid vector + keyword)  │
    │  → "Return and Refund Policy"│
    └──────────────┬──────────────┘
                   │
                   ▼
    ┌─────────────────────────────┐
    │  3. GENERATE answer         │
    │  Chat model receives:       │
    │  - User question            │
    │  - Retrieved policy docs    │
    │  → Grounded, accurate reply │
    └─────────────────────────────┘
```

## Architecture — How It All Fits Together

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Azure AI Foundry                            │
│  ┌──────────────────────┐    ┌───────────────────────────────┐     │
│  │  Chat Model (gpt-4.1)│    │  Embedding Model (ada-002)    │     │
│  │  "The Brain"         │    │  "The Translator"             │     │
│  └──────────┬───────────┘    └──────────────┬────────────────┘     │
└─────────────┼───────────────────────────────┼──────────────────────┘
              │                               │
              │  3. Generate answer            │  Called by AI Search
              │     using retrieved docs       │  skillset (integrated)
              │                               │
┌─────────────┼───────────────────────────────┼──────────────────────┐
│             │                               │                      │
│  ┌──────────▼───────────┐    ┌──────────────▼────────────────┐     │
│  │  Cosmos DB - CRM         │    │  Azure AI Search (Basic)      │     │
│  │  (Session)               │    │  Index: knowledge-documents   │     │
│  │  Customers, Orders,      │    │  Skillset: split + embed      │     │
│  │  Products, etc.           │    │  Indexer: blob → index        │     │
│  │  ← NoSQL queries         │    │  ← Hybrid search (RAG)       │     │
│  └──────────────────────┘    └───────────────────────────────┘     │
│                                                                    │
│  ┌──────────────────────┐    Cosmos DB — Agents                    │
│  │  Agents Account      │  ← conversation memory (runtime only)   │
│  │  (Eventual)          │                                          │
│  │  Agent State Store   │                                          │
│  └──────────────────────┘                                          │
└────────────────────────────────────────────────────────────────────┘
              ▲                               ▲
              │                               │
     ┌────────┴────────┐            ┌─────────┴──────────┐
     │  contoso-crm/   │            │  Azure Blob Storage │
     │  (CSV exports)  │            │  sharepoint-docs    │
     └─────────────────┘            │  (PDFs by Terraform)│
                                    └────────────────────┘
```

## Data flow

### Structured data (Store Data → Cosmos DB)

`contoso-crm/` simulates a store data export — customer records, orders, products, etc. as CSV files. The seed tool parses these and upserts them into **Cosmos DB** containers as JSON documents. Agents query this data using tools with NoSQL queries (e.g., "find all orders for customer 101"). CRM seeding runs automatically during `terraform apply` via a `null_resource` with `local-exec`.

### Unstructured data (SharePoint → Azure AI Search)

`contoso-sharepoint/` simulates a SharePoint document library — policy documents, procedures, and guides stored as PDFs (just like in a real enterprise). Terraform uploads these PDFs to the `sharepoint-docs` blob container during `terraform apply`. The **Azure AI Search indexer** then processes them automatically via integrated vectorization:

1. **Extracts text** from each PDF (built-in document cracking)
2. **Chunks the text** into ~500-token segments (Text Split skill)
3. **Generates a vector** for each chunk via the Azure OpenAI Embedding skill (`text-embedding-ada-002`, 1536 dimensions)
4. **Indexes each chunk** in the `knowledge-documents` search index

The AI Search index is configured with:
- **HNSW** vector search algorithm
- `cosine` distance metric, `float32` data type, 1536 dimensions
- Hybrid search (vector + keyword) for improved relevance

**Event Grid** triggers the indexer on new blob uploads for near-instant availability. The indexer also runs on a 5-minute schedule as a fallback.

### Conversation history (Cosmos DB — runtime only)

The `conversations` container lives in the **Agents** Cosmos DB account (Eventual consistency). It is **not seeded** — it's populated at runtime by the BFF as users chat with agents. The BFF is the sole owner of conversation persistence. Agents are stateless — they receive conversation history in each request.

- **Container:** `conversations`
- **Partition key:** `/sessionId`
- **Written by:** BFF (saves user messages + agent responses + tool calls)
- **Read by:** BFF (loads history for chat panel + passes to orchestrator)

## Seeding & Indexing

### Prerequisites

See [Lab 1](../docs/lab-1.md) for prerequisites and step-by-step instructions.

### How data gets loaded

All data loading happens automatically during `terraform apply`:

**1. Product images → Blob Storage (Terraform)**

Product images from `contoso-images/` are uploaded to the `product-images` blob container.

**2. SharePoint PDFs → Blob Storage → AI Search (Terraform + indexer)**

```
contoso-sharepoint/**/*.pdf
        │
        ▼
   Terraform uploads to sharepoint-docs blob container
        │
        ▼
   AI Search indexer (triggered by Event Grid or 5-min schedule):
        │
        ├──► Document cracking (extract text from PDF)
        ├──► Text Split skill (chunk ~500 tokens, 200 overlap)
        ├──► Azure OpenAI Embedding skill (ada-002 → 1536-dim vector)
        └──► Index in knowledge-documents
```

**3. CRM data → Cosmos DB (Terraform local-exec → seed tool)**

```
contoso-crm/customers.csv      ──parse CSV──►  Cosmos DB / Customers container
contoso-crm/orders.csv         ──parse CSV──►  Cosmos DB / Orders container
...etc for all 6 CSV files
```

Each CSV row becomes a JSON document. The seed tool creates containers with proper partition keys, then upserts documents. The seed runs automatically via `null_resource.seed_crm` and only re-runs when CSV data changes (tracked via file hash).

## Folder structure

```
data/
├── contoso-crm/                    ← Simulated store data export (structured)
│   ├── customers.csv
│   ├── orders.csv
│   ├── order-items.csv
│   ├── products.csv
│   ├── promotions.csv
│   └── support-tickets.csv
│
├── contoso-sharepoint/             ← Simulated SharePoint document library (unstructured)
│   ├── policies/                   ← Policy documents (.txt source + .pdf)
│   │   ├── return-and-refund-policy.txt / .pdf
│   │   ├── warranty-policy.txt / .pdf
│   │   ├── price-match-policy.txt / .pdf
│   │   └── loyalty-program-terms.txt / .pdf
│   ├── procedures/                 ← Procedure documents (.txt source + .pdf)
│   │   ├── processing-a-return.txt / .pdf
│   │   ├── filing-a-warranty-claim.txt / .pdf
│   │   └── exchanging-a-product.txt / .pdf
│   └── guides/                     ← Customer guides (.txt source + .pdf)
│       ├── boot-sizing-guide.txt / .pdf
│       ├── tent-selection-guide.txt / .pdf
│       ├── layering-guide.txt / .pdf
│       ├── backpack-fitting-guide.txt / .pdf
│       └── gear-care-and-maintenance.txt / .pdf
│
└── contoso-images/                 ← Product images (uploaded to Azure Blob Storage)
    └── *.png                       ← Product photos referenced by products.csv image_filename
```

The `.txt` files are the editable source content. The `.pdf` files are uploaded to Azure Blob Storage by Terraform and indexed by the AI Search indexer.

## Data mapping

| Source | Destination | How |
|--------|-------------|-----|
| `contoso-crm/customers.csv` | Cosmos DB / Customers | Seed tool (local-exec) |
| `contoso-crm/orders.csv` | Cosmos DB / Orders | Seed tool (local-exec) |
| `contoso-crm/order-items.csv` | Cosmos DB / OrderItems | Seed tool (local-exec) |
| `contoso-crm/products.csv` | Cosmos DB / Products | Seed tool (local-exec) |
| `contoso-crm/promotions.csv` | Cosmos DB / Promotions | Seed tool (local-exec) |
| `contoso-crm/support-tickets.csv` | Cosmos DB / SupportTickets | Seed tool (local-exec) |
| `contoso-sharepoint/**/*.pdf` | Azure AI Search / `knowledge-documents` index | Terraform blob upload → AI Search indexer |
| `contoso-images/*.png` | Azure Blob Storage / `product-images` container | Terraform blob upload |
| (runtime) | Cosmos DB Agents / conversations (`/sessionId`) | BFF (conversation history) |

## Scenario data

The store data includes 8 deterministic customer scenarios designed to test specific agent capabilities:

| # | Customer | Scenario |
|---|----------|----------|
| 1 | Emma Wilson (101) | Order shipped — tracking and delivery status inquiry |
| 2 | James Chen (102) | Hiking boots don't fit — return eligibility and sizing help |
| 3 | Sarah Miller (103) | Gold loyalty member looking for tent deals |
| 4 | David Park (104) | Damaged jacket received — warranty claim and support ticket |
| 5 | Lisa Torres (105) | Backpack recommendation for multi-day trip |
| 6 | Mike Johnson (106) | Gear care advice for newly delivered tent |
| 7 | Anna Roberts (107) | Order cancellation request (still processing) |
| 8 | Tom Garcia (108) | Returned items — refund status inquiry |
