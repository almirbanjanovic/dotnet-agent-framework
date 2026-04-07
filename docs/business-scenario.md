# Contoso Outdoors — Business Scenario

This document describes the business scenario for the .NET Agent Framework workshop.

## 1. Business Scenario

**Contoso Outdoors** is an outdoor and adventure gear retailer. Customers browse products, place orders, track deliveries, request returns, and ask about gear recommendations. The company sells tents, backpacks, footwear, clothing, cooking gear, and accessories for hiking, camping, and trail running.

Customer service agents handle requests ranging from simple product lookups to multi-step scenarios like processing a return, recommending the right gear for a trip, or explaining why an order is delayed. Customers interact directly with AI agents through a self-service portal — when a customer logs in and asks a question, the agent already knows who they are (from their authentication token) and can access their orders, profile, and account history. Agents also have access to the product catalog, knowledge base (sizing guides, care instructions, policies), and product images stored in Azure Blob Storage.

## 2. Backend Systems

| System | Description | Data Types |
| -------- | ------------- | ------------ |
| **Customer Database** | Customer profiles with loyalty tiers and account status | Names, emails, addresses, loyalty tier (Bronze/Silver/Gold/Platinum) |
| **Order Management** | Orders with line items, shipping status, and tracking | Order status (placed/processing/shipped/delivered/returned/cancelled), tracking numbers, delivery estimates |
| **Product Catalog** | Full product inventory with descriptions, pricing, and availability | Product details, categories, stock status, ratings, images |
| **Promotions Engine** | Active sales, discounts, and loyalty-tier-specific deals | Discount percentages, eligible categories, loyalty tier requirements |
| **Support Tickets** | Customer support cases tied to orders | Ticket status, priority, category (shipping/product-issue/return/general) |
| **Knowledge Base** | Guides, policies, and procedures as searchable documents | Sizing guides, care instructions, return policies, warranty info (vector-searchable via Azure AI Search) |
| **Product Image Store** | Product photos stored in Azure Blob Storage | High-quality product images proxied through BFF to browser (agents include `imageFilename` in markdown responses) |

## 3. Customer Scenarios

These scenarios are seeded into the data and have deterministic expected outcomes. Use them to test and validate agent behavior.

---

### Scenario 1 — "Where's my order?"

**Customer:** Emma Wilson (ID: 101)
**Question:** *"I placed an order a few days ago — can you tell me where it is?"*

**What the agent should do:**

1. Retrieve Emma's orders (agent knows her identity from the auth token) → find Order 1001 (status: `shipped`, tracking: `TRK-29481`)
2. Report the order is shipped with tracking number and estimated delivery date

**Systems accessed:** Customer Database, Order Management
**Agent route:** Orchestrator → CRM Agent

---

### Scenario 2 — "These boots don't fit"

**Customer:** James Chen (ID: 102)
**Question:** *"I got my hiking boots but they're too small. Can I return them?"*

**What the agent should do:**

1. Retrieve James's recent orders → find Order 1002 with "TrailBlazer Hiking Boots" (delivered 5 days ago)
2. Check the return policy in the knowledge base → within 30-day return window
3. Confirm eligibility, explain the return process
4. Offer the boot sizing guide to help pick the right size for a replacement

**Systems accessed:** Customer Database, Order Management, Knowledge Base (return policy + boot sizing guide)
**Agent route:** Orchestrator → CRM Agent

---

### Scenario 3 — "Any deals on tents?"

**Customer:** Sarah Miller (ID: 103)
**Question:** *"I'm a Gold member — are there any tent deals right now?"*

**What the agent should do:**

1. Retrieve Sarah's profile → confirm Gold loyalty tier
2. Check promotions → find "Spring Camping Sale" (15% off tents, available to Silver+ members)
3. Search products in the "tents" category → show matching products with images
4. Confirm Sarah qualifies for the promotion based on her loyalty tier

**Systems accessed:** Customer Database, Promotions, Product Catalog
**Agent route:** Orchestrator → Product Agent
**Note:** Product images are rendered by the Blazor WASM UI from the `imageFilename` field — agents do not retrieve image bytes.

---

### Scenario 4 — "My jacket arrived damaged"

**Customer:** David Park (ID: 104)
**Question:** *"The jacket I ordered arrived with a tear in the sleeve. What can I do?"*

**What the agent should do:**

1. Retrieve David's orders → find Order 1004 with "Alpine Summit Jacket" (delivered)
2. Check warranty policy in knowledge base → covers manufacturing defects
3. Create a support ticket (category: product-issue, priority: high)
4. Explain options: replacement or refund per the return/exchange procedure

**Systems accessed:** Customer Database, Order Management, Knowledge Base (warranty policy + exchange procedure), Support Tickets
**Agent route:** Orchestrator → CRM Agent
**Note:** Any authenticated customer can create support tickets for their own orders.

---

### Scenario 5 — "Help me pick a backpack"

**Customer:** Lisa Torres (ID: 105)
**Question:** *"I'm planning a 5-day backpacking trip. What backpack should I get?"*

**What the agent should do:**

1. Search the knowledge base for the backpack fitting guide → recommend 60-75L capacity for multi-day trips
2. Search products in the "backpacks" category → show options with ratings and prices
3. Show product images for the recommended backpacks
4. Reference the fitting guide for torso measurement and hip belt adjustment

**Systems accessed:** Product Catalog, Knowledge Base (backpack fitting guide)
**Agent route:** Orchestrator → Product Agent
**Note:** Product images are rendered by the Blazor WASM UI from the `imageFilename` field.

### Scenario 6 — "How do I waterproof my tent?"

**Customer:** Mike Johnson (ID: 106)
**Question:** *"I just got my tent delivered. How should I take care of it?"*

**What the agent should do:**

1. Confirm Mike's order → find Order 1006 with "Basecamp 4P Tent" (delivered)
2. Search knowledge base for gear care and maintenance guide
3. Provide waterproofing, cleaning, and storage recommendations from the guide

**Systems accessed:** Customer Database, Order Management, Knowledge Base (gear care guide)
**Agent route:** Orchestrator → CRM Agent

---

### Scenario 7 — "I want to cancel my order"

**Customer:** Anna Roberts (ID: 107)
**Question:** *"I changed my mind about my order. Can I cancel it?"*

**What the agent should do:**

1. Retrieve Anna's orders → find Order 1007 (status: `processing` — not yet shipped)
2. Since status is `processing`, explain that cancellation is possible before shipping
3. Create a support ticket for the cancellation request (category: general, priority: medium)

**Systems accessed:** Customer Database, Order Management, Support Tickets

---

### Scenario 8 — "What happened to my refund?"

**Customer:** Tom Garcia (ID: 108)
**Question:** *"I returned some items a while ago but haven't gotten my refund yet."*

**What the agent should do:**

1. Retrieve Tom's orders → find Order 1008 (status: `returned`)
2. Check existing support tickets → find open ticket about the refund
3. Reference the return policy for refund processing timelines (5-10 business days)
4. Provide status update based on the support ticket

**Systems accessed:** Customer Database, Order Management, Support Tickets, Knowledge Base (return policy)

---

## 4. MCP Server Design

### CRM MCP Server

All CRM tools in a single MCP server — wraps CRM API endpoints for customer profiles, order management, support tickets, product catalog, and promotions.

| Tool | Description |
| ------ | ------------- |
| `get_all_customers` | List all customers with basic info (name, email, loyalty tier) |
| `get_customer_detail(customer_id)` | Full customer profile |
| `get_customer_orders(customer_id)` | All orders for a customer |
| `get_order_detail(order_id)` | Order with line items, shipping status, tracking number |
| `get_products(query?, category?, in_stock_only?)` | Search or browse products |
| `get_product_detail(product_id)` | Full product info including description, price, rating |
| `get_promotions()` | All active promotions |
| `get_eligible_promotions(customer_id)` | Promotions matching customer's loyalty tier |
| `get_support_tickets(customer_id, open_only?)` | Customer's support tickets |
| `create_support_ticket(customer_id, order_id, category, priority, subject, description)` | Create a new support ticket |

### Knowledge Base MCP Server

Semantic search over guides, policies, and procedures (RAG pattern).

| Tool | Description |
| ------ | ------------- |
| `search_knowledge_base(query, top_k?)` | Vector similarity search on knowledge documents |

## 5. Data Model

### Structured Data (CSV → Cosmos DB)

| Entity | Fields | Primary Key |
| -------- | -------- | ------------- |
| **Customers** | `id`, `first_name`, `last_name`, `email`, `phone`, `address`, `loyalty_tier`, `account_status`, `created_date` | `id` |
| **Orders** | `id`, `customer_id`, `order_date`, `status`, `total_amount`, `shipping_address`, `tracking_number`, `estimated_delivery` | `id` (FK: `customer_id` \u2192 Customers) |
| **OrderItems** | `id`, `order_id`, `product_id`, `product_name`, `quantity`, `unit_price` | `id` (FK: `order_id` \u2192 Orders) |
| **Products** | `id`, `name`, `category`, `description`, `price`, `in_stock`, `rating`, `weight_kg`, `image_filename` | `id` |
| **Promotions** | `id`, `name`, `description`, `discount_percent`, `eligible_categories`, `min_loyalty_tier`, `start_date`, `end_date`, `active` | `id` |
| **SupportTickets** | `id`, `customer_id`, `order_id`, `category`, `subject`, `description`, `status`, `priority`, `opened_at`, `closed_at` | `id` (FK: `customer_id` \u2192 Customers, `order_id` \u2192 Orders) |

### Unstructured Data (PDFs → Azure AI Search via integrated vectorization)

Policy documents, guides, and procedures are uploaded to Azure Blob Storage and automatically vectorized by the AI Search indexer (text extraction → chunking → embedding). Stored in the `knowledge-documents` search index for semantic search.

### Product Images (Azure Blob Storage)

Product photos are stored in a `product-images` blob container. The BFF proxies image bytes to the browser — no direct storage URL is exposed. Agents reference images by including the `imageFilename` from product data in their markdown responses.

## 6. Knowledge Base Documents

### Policies

- **Return and Refund Policy** — 30-day return window, condition requirements, refund timelines (5-10 business days)
- **Warranty Policy** — Manufacturer vs. Contoso coverage, defect claims, proof of purchase requirements
- **Price Match Policy** — Competitor price matching rules, exclusions, time limits
- **Loyalty Program Terms** — Tier thresholds (Bronze/Silver/Gold/Platinum), earning points, tier benefits

### Guides

- **Boot Sizing Guide** — Size conversion charts (US/EU/UK), width options, break-in tips
- **Tent Selection Guide** — Capacity ratings, 3-season vs 4-season, weight considerations, vestibule options
- **Layering Guide** — Base/mid/outer layer system, material recommendations by activity and temperature
- **Backpack Fitting Guide** — Torso measurement, hip belt adjustment, load distribution, capacity by trip length
- **Gear Care and Maintenance** — Cleaning, waterproofing, storage, when to replace gear

### Procedures

- **Processing a Return** — Step-by-step return procedure: verify eligibility, generate shipping label, inspect item, process refund
- **Filing a Warranty Claim** — Required documentation, timelines, replacement vs repair decision tree
- **Exchanging a Product** — Size/color exchange process, stock check, shipping for exchanges
