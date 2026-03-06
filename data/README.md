# Data

This folder contains simulated enterprise data sources for the Contoso Outdoors agent framework. The data represents what you'd find in a real company's systems — a product/order database, a document library, and a product image store.

> **This data is used to seed Cosmos DB and Azure Blob Storage.** The seed tool (`src/seed-data/`) reads from these folders, vectorizes unstructured documents, and populates the databases.

## What is RAG?

**RAG (Retrieval-Augmented Generation)** is the pattern that connects your company's data to a large language model. Without RAG, the chat model only knows what it was trained on. With RAG, it can answer questions about *your* specific policies, customers, and data — without fine-tuning the model.

The key insight: you can't search unstructured text (like a PDF policy document) with a database query. If a customer asks *"what is your return policy?"*, a keyword search for those exact words would return nothing — because the document is titled "Return and Refund Policy" and uses different phrasing.

**Vector embeddings solve this.** An embedding model converts text into numerical vectors that capture *meaning*, not exact words. Two texts that are semantically similar produce vectors that are close together — even if they share no words. This enables semantic search: find documents by meaning, not by keyword match.

### The two models

| Model | Role | Used when |
|-------|------|-----------|
| **Embedding model** (text-embedding-ada-002) | Converts text → 1536-dim vector | Seeding (documents) and query time (user questions) |
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
    │  Cosmos DB vector search    │
    │  (VectorDistance)           │
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
              │  3. Generate answer            │  1. Convert to vector
              │     using retrieved docs       │
              │                               │
┌─────────────┼───────────────────────────────┼──────────────────────┐
│             │   Cosmos DB (3 accounts)      │                      │
│             │                               │                      │
│  ┌──────────▼───────────┐    ┌──────────────▼────────────────┐     │
│  │  Operational Account │    │  Knowledge Account           │     │
│  │  (Session consistency)│   │  (Eventual + Vector Search)  │     │
│  │  Customers, Orders,  │    │  KnowledgeDocuments          │     │
│  │  Products, etc.      │    │  (vectorized PDFs)           │     │
│  │  ← SQL queries       │    │  ← VectorDistance (RAG)      │     │
│  └──────────────────────┘    └───────────────────────────────┘     │
│                                                                    │
│  ┌──────────────────────┐                                          │
│  │  Agents Account      │  ← conversation memory (runtime only)   │
│  │  (Eventual)          │                                          │
│  │  Agent State Store   │                                          │
│  └──────────────────────┘                                          │
└────────────────────────────────────────────────────────────────────┘
              ▲                               ▲
              │                               │
     ┌────────┴────────┐            ┌─────────┴──────────┐
     │  contoso-crm/   │            │ contoso-sharepoint/ │
     │  (CSV exports)  │            │ (PDF docs + source) │
     └─────────────────┘            └────────────────────┘
```

## Data flow

### Structured data (Store Data → Operational Account)

`contoso-crm/` simulates a store data export — customer records, orders, products, etc. as CSV files. The seed tool parses these and upserts them into the **Operational** Cosmos DB account (Session consistency). **No vectorization is needed.** Agents query this data using tools with standard SQL queries (e.g., "find all orders for customer 101").

### Unstructured data (SharePoint → Knowledge Account)

`contoso-sharepoint/` simulates a SharePoint document library — policy documents, procedures, and guides stored as PDFs (just like in a real enterprise). The seed tool writes to the **Knowledge** Cosmos DB account (Eventual consistency, vector search enabled):

1. **Extracts text** from each PDF using a PDF reader library (e.g., PdfPig)
2. **Chunks the text** into segments that fit within the embedding model's token limit
3. **Generates a vector** for each chunk by sending it through the embedding model (`text-embedding-ada-002`), producing a 1536-dimensional float array
4. **Stores each chunk** as a document in the `KnowledgeDocuments` container with its `content_vector` field

The `KnowledgeDocuments` container is configured on the **Knowledge** account with:
- `EnableNoSQLVectorSearch` capability on the Cosmos DB account
- A **diskANN** vector index on the `/content_vector` path
- `cosine` distance function, `float32` data type, 1536 dimensions
- The `/content_vector/*` path is excluded from regular indexing (it only participates in vector queries)

### Agent state (Agents Account — runtime only)

The `workshop_agent_state_store` container lives in the **Agents** Cosmos DB account (Eventual consistency). It is **not seeded** — it's populated at runtime as agents have conversations. It persists conversation history and agent memory across sessions.

## Seeding Cosmos DB

### Prerequisites

Before seeding, ensure:
1. Infrastructure is deployed (see [infra/README.md](../infra/README.md))
2. The Cosmos DB account, database, and all containers exist
3. You have run `config-sync` to populate `src/appsettings.json` (see [src/README.md](../src/README.md))

### How the seed tool works

The seed tool (`src/seed-data/`) performs two distinct operations:

**1. Load structured data (Store Data → Operational account)**

```
contoso-crm/customers.csv      ──parse CSV──►  Operational / "Customers" container
contoso-crm/orders.csv         ──parse CSV──►  Operational / "Orders" container
...etc for all 6 CSV files
```

Each CSV row becomes a JSON document. The seed tool maps CSV column names to JSON properties and upserts into the corresponding container using the correct partition key.

**2. Vectorize and load unstructured data (SharePoint → Knowledge account)**

```
contoso-sharepoint/**/*.pdf
        │
        ▼
   Extract text from PDF
        │
        ▼
   Chunk into segments (~500 tokens each)
        │
        ▼
   For each chunk:
        │
        ├──► Call embedding model (text-embedding-ada-002)
        │    → returns 1536-dim float[] vector
        │
        └──► Upsert to Knowledge / "KnowledgeDocuments"
             {
               "id": "return-and-refund-policy-chunk-1",
               "title": "Return And Refund Policy",
               "category": "policy",
               "source_file": "policies/return-and-refund-policy.pdf",
               "content": "Contoso Outdoors accepts returns within 30 calendar days...",
               "content_vector": [0.0123, -0.0456, 0.0789, ...]  // 1536 floats
             }
```

### Running the seed tool

From `src/seed-data/`:

```bash
dotnet run
```

The seed tool reads connection settings from `src/appsettings.json` (same config as the agent labs).

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
│   ├── generate-pdfs/              ← .NET tool to regenerate PDFs from .txt sources
│   ├── policies/                   ← Policy documents (.txt source + .pdf generated)
│   │   ├── return-and-refund-policy.txt / .pdf
│   │   ├── warranty-policy.txt / .pdf
│   │   ├── price-match-policy.txt / .pdf
│   │   └── loyalty-program-terms.txt / .pdf
│   ├── procedures/                 ← Procedure documents (.txt source + .pdf generated)
│   │   ├── processing-a-return.txt / .pdf
│   │   ├── filing-a-warranty-claim.txt / .pdf
│   │   └── exchanging-a-product.txt / .pdf
│   └── guides/                     ← Customer guides (.txt source + .pdf generated)
│       ├── boot-sizing-guide.txt / .pdf
│       ├── tent-selection-guide.txt / .pdf
│       ├── layering-guide.txt / .pdf
│       ├── backpack-fitting-guide.txt / .pdf
│       └── gear-care-and-maintenance.txt / .pdf
│
└── contoso-images/                 ← Product images (uploaded to Azure Blob Storage)
    └── *.jpg                       ← Product photos referenced by products.csv image_filename
```

The `.txt` files are the editable source content. The `.pdf` files are generated from them using the `generate-pdfs` tool. To regenerate PDFs after editing a `.txt` file:

```bash
cd data/contoso-sharepoint/generate-pdfs
dotnet run
```

## Cosmos DB container mapping

| Account | Source | Container | Partition key | Vectorized |
|---------|--------|-----------|---------------|------------|
| Operational | `contoso-crm/customers.csv` | Customers | `/id` | No |
| Operational | `contoso-crm/orders.csv` | Orders | `/customer_id` | No |
| Operational | `contoso-crm/order-items.csv` | OrderItems | `/order_id` | No |
| Operational | `contoso-crm/products.csv` | Products | `/category` | No |
| Operational | `contoso-crm/promotions.csv` | Promotions | `/id` | No |
| Operational | `contoso-crm/support-tickets.csv` | SupportTickets | `/customer_id` | No |
| Knowledge | `contoso-sharepoint/**/*.pdf` | KnowledgeDocuments | `/id` | **Yes** |
| Agents | (runtime) | workshop_agent_state_store | `/tenant_id`, `/id` | No |

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
