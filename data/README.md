# Data

This folder contains simulated enterprise data sources for the Contoso Telecom agent framework. The data represents what you'd find in a real company's systems — a CRM and a document library.

> **This data is used to seed Cosmos DB.** The seed tool (`src/seed-data/`) reads from these folders, vectorizes unstructured documents, and populates the database.

## What is RAG?

**RAG (Retrieval-Augmented Generation)** is the pattern that connects your company's data to a large language model. Without RAG, the chat model only knows what it was trained on. With RAG, it can answer questions about *your* specific policies, customers, and data — without fine-tuning the model.

The key insight: you can't search unstructured text (like a PDF policy document) with a database query. If a customer asks *"what happens if I go over my data limit?"*, a keyword search for those exact words would return nothing — because the document is titled "Data Overage Policy" and uses different phrasing.

**Vector embeddings solve this.** An embedding model converts text into numerical vectors that capture *meaning*, not exact words. Two texts that are semantically similar produce vectors that are close together — even if they share no words. This enables semantic search: find documents by meaning, not by keyword match.

### The two models

| Model | Role | Used when |
|-------|------|-----------|
| **Embedding model** (text-embedding-ada-002) | Converts text → 1536-dim vector | Seeding (documents) and query time (user questions) |
| **Chat model** (gpt-4.1) | Generates natural language answers | Query time — receives user question + retrieved documents as context |

### The RAG flow

```
User: "What happens if I go over my data limit?"
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
    │  → "Data Overage Policy"    │
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
│             │        Cosmos DB              │                      │
│  ┌──────────▼───────────┐    ┌──────────────▼────────────────┐     │
│  │  Structured Data     │    │  KnowledgeDocuments           │     │
│  │  (Customers, Orders, │    │  (vectorized content from     │     │
│  │   Invoices, etc.)    │    │   SharePoint PDFs)            │     │
│  │                      │    │                               │     │
│  │  ← queried by tools  │    │  ← 2. Vector similarity      │     │
│  │    (SQL queries)     │    │       search (RAG retrieval)  │     │
│  └──────────────────────┘    └───────────────────────────────┘     │
│                                                                    │
│  ┌──────────────────────┐                                          │
│  │  Agent State Store   │  ← conversation memory (runtime only)   │
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

### Structured data (CRM → Cosmos DB)

`contoso-crm/` simulates a CRM export — customer records, subscriptions, invoices, etc. as CSV files. The seed tool parses these and upserts them into their respective Cosmos DB containers. **No vectorization is needed.** Agents query this data using tools with standard SQL queries (e.g., "find all invoices for customer 251").

### Unstructured data (SharePoint → Cosmos DB with vectors)

`contoso-sharepoint/` simulates a SharePoint document library — policy documents, procedures, and guides stored as PDFs (just like in a real enterprise). The seed tool:

1. **Extracts text** from each PDF using a PDF reader library (e.g., PdfPig)
2. **Chunks the text** into segments that fit within the embedding model's token limit
3. **Generates a vector** for each chunk by sending it through the embedding model (`text-embedding-ada-002`), producing a 1536-dimensional float array
4. **Stores each chunk** as a document in the `KnowledgeDocuments` container with its `content_vector` field

The `KnowledgeDocuments` container is configured with:
- `EnableNoSQLVectorSearch` capability on the Cosmos DB account
- A **diskANN** vector index on the `/content_vector` path
- `cosine` distance function, `float32` data type, 1536 dimensions
- The `/content_vector/*` path is excluded from regular indexing (it only participates in vector queries)

### Agent state (runtime only)

The `workshop_agent_state_store` container is **not seeded** — it's populated at runtime as agents have conversations. It persists conversation history and agent memory across sessions.

## Seeding Cosmos DB

### Prerequisites

Before seeding, ensure:
1. Infrastructure is deployed (see [infra/README.md](../infra/README.md))
2. The Cosmos DB account, database, and all containers exist
3. You have run `config-sync` to populate `src/appsettings.json` (see [src/README.md](../src/README.md))

### How the seed tool works

The seed tool (`src/seed-data/`) performs two distinct operations:

**1. Load structured data (CRM → containers)**

```
contoso-crm/customers.csv  ──parse CSV──►  Cosmos DB "Customers" container
contoso-crm/invoices.csv   ──parse CSV──►  Cosmos DB "Invoices" container
...etc for all 11 CSV files
```

Each CSV row becomes a JSON document. The seed tool maps CSV column names to JSON properties and upserts into the corresponding container using the correct partition key.

**2. Vectorize and load unstructured data (SharePoint → KnowledgeDocuments)**

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
        └──► Upsert to Cosmos DB "KnowledgeDocuments"
             {
               "id": "data-overage-policy-chunk-1",
               "title": "Data Overage Policy",
               "category": "policy",
               "source_file": "policies/data-overage-policy.pdf",
               "content": "When subscribers exceed the monthly data...",
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
├── contoso-crm/                    ← Simulated CRM export (structured)
│   ├── customers.csv
│   ├── subscriptions.csv
│   ├── products.csv
│   ├── promotions.csv
│   ├── invoices.csv
│   ├── payments.csv
│   ├── orders.csv
│   ├── support-tickets.csv
│   ├── data-usage.csv
│   ├── service-incidents.csv
│   └── security-logs.csv
│
└── contoso-sharepoint/             ← Simulated SharePoint document library (unstructured)
    ├── generate-pdfs/              ← .NET tool to regenerate PDFs from .txt sources
    ├── policies/                   ← Policy documents (.txt source + .pdf generated)
    │   ├── data-overage-policy.txt / .pdf
    │   ├── billing-dispute-escalation.txt / .pdf
    │   ├── late-payment-fee-policy.txt / .pdf
    │   ├── promotion-eligibility-guidelines.txt / .pdf
    │   ├── payment-failure-reinstatement.txt / .pdf
    │   ├── service-reliability-sla.txt / .pdf
    │   └── autopay-discount-terms.txt / .pdf
    ├── procedures/                 ← Procedure documents (.txt source + .pdf generated)
    │   ├── account-unlock-procedure.txt / .pdf
    │   ├── troubleshooting-slow-internet.txt / .pdf
    │   ├── return-policy-and-process.txt / .pdf
    │   ├── financial-hardship-payment-plan.txt / .pdf
    │   └── dropped-call-investigation.txt / .pdf
    └── guides/                     ← Customer guides (.txt source + .pdf generated)
        ├── international-roaming-guide.txt / .pdf
        ├── router-reset-guide.txt / .pdf
        ├── understanding-your-bill.txt / .pdf
        ├── upgrading-internet-speed.txt / .pdf
        ├── account-security-best-practices.txt / .pdf
        └── text-messaging-troubleshooting.txt / .pdf
```

The `.txt` files are the editable source content. The `.pdf` files are generated from them using the `generate-pdfs` tool. To regenerate PDFs after editing a `.txt` file:

```bash
cd data/contoso-sharepoint/generate-pdfs
dotnet run
```

## Cosmos DB container mapping

| Source | Cosmos DB container | Partition key | Vectorized |
|--------|-------------------|---------------|------------|
| `contoso-crm/customers.csv` | Customers | `/id` | No |
| `contoso-crm/subscriptions.csv` | Subscriptions | `/customer_id` | No |
| `contoso-crm/products.csv` | Products | `/category` | No |
| `contoso-crm/promotions.csv` | Promotions | `/id` | No |
| `contoso-crm/invoices.csv` | Invoices | `/subscription_id` | No |
| `contoso-crm/payments.csv` | Payments | `/invoice_id` | No |
| `contoso-crm/orders.csv` | Orders | `/customer_id` | No |
| `contoso-crm/support-tickets.csv` | SupportTickets | `/customer_id` | No |
| `contoso-crm/data-usage.csv` | DataUsage | `/subscription_id` | No |
| `contoso-crm/service-incidents.csv` | ServiceIncidents | `/subscription_id` | No |
| `contoso-crm/security-logs.csv` | SecurityLogs | `/customer_id` | No |
| `contoso-sharepoint/**/*.pdf` | KnowledgeDocuments | `/id` | **Yes** |

## Scenario data

The CRM data includes 9 deterministic customer scenarios designed to test specific agent capabilities:

| # | Customer | Scenario |
|---|----------|----------|
| 1 | John Doe | High data usage leading to overage charges on internet plan |
| 2 | Jane Doe | Slow internet speeds with active service incident |
| 3 | Mark Doe | Standard active mobile plan customer |
| 4 | Alice Doe | Account locked after 8 failed login attempts |
| 5 | Ron Doe | Active mobile customer (Gold loyalty) |
| 6 | Mary Doe | Returned order requiring refund processing |
| 7 | Tom Smith | Repeated call drops — open support ticket |
| 8 | Sara Lee | Inactive subscription with failed payments |
| 9 | Alex Brown | Mobile plan with data overage (roaming enabled) |
