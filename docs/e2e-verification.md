# E2E Verification Checklist: 8 Customer Scenarios

This document maps all 8 business scenarios to executable test steps using the local dev stack.

**Last Updated:** 2026-03-24  
**Tester:** Peter  
**Status:** Ready for validation

---

## Prerequisites

Before running any scenario:

1. **Local dev stack is running:**
   - [ ] `dotnet run --project src/AppHost` is executing in terminal
   - [ ] All 8 services are healthy and ready

2. **Service endpoints are responding:**
   - [ ] Aspire Dashboard: http://localhost:15000 → shows all 8 services in "Running" state
   - [ ] Blazor UI: http://localhost:5008 → loads without auth errors
   - [ ] BFF API health: `curl http://localhost:5007/health` → 200 OK
   - [ ] CRM API health: `curl http://localhost:5001/api/v1/health` → 200 OK

3. **Seed data is loaded:**
   - [ ] `curl http://localhost:5001/api/v1/customers` → returns all 8+ customers (JSON array, 200 OK)
   - [ ] `curl http://localhost:5001/api/v1/customers/101` → Emma Wilson profile present
   - [ ] All 8 customer IDs (101–108) exist in the response

4. **MCP servers are connected:**
   - [ ] Orchestrator dashboard logs show "CRM MCP: connected" on startup
   - [ ] Orchestrator dashboard logs show "Knowledge MCP: connected" on startup

5. **UI is authenticated and ready:**
   - [ ] Navigate to http://localhost:5008
   - [ ] Dev auth dropdown shows customer IDs 101–108
   - [ ] Default customer selected: 101 (Emma Wilson)
   - [ ] Chat input box is active and focused

---

## Smoke Test Checklist

Run these checks before executing scenarios. Record results below.

```
Date: _______________
Tester: _______________

[ ] All services healthy in Aspire Dashboard (15000)
[ ] Blazor UI loads without 401/403 errors
[ ] BFF /health returns 200
[ ] CRM API /health returns 200
[ ] CRM API /api/v1/customers returns 200 with ≥8 customers
[ ] Dev auth dropdown populated with IDs 101–108
[ ] Chat input box is focusable and ready
[ ] Orchestrator logs show both MCPs connected
[ ] Knowledge base index is online (Product Agent startup logs confirm)
[ ] System ready for E2E testing
```

---

## Scenario 1: "Where's my order?"

**Customer:** Emma Wilson (ID: 101)  
**User Query:** "I placed an order a few days ago — can you tell me where it is?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders` → Order detail extraction

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Navigate to http://localhost:5008
   - [ ] Dev auth dropdown shows "101 — Emma Wilson"
   - [ ] Select customer ID 101

2. **Send query to chat:**
   - [ ] Type: "I placed an order a few days ago — can you tell me where it is?"
   - [ ] Press Enter
   - [ ] Record response time: _____ ms

3. **Verify Orchestrator routing:**
   - [ ] Check Orchestrator logs for: `Routing to CRM Agent` or similar
   - [ ] Verify CRM Agent was invoked (logs show agent initialization)

4. **Verify MCP tool calls:**
   - [ ] CRM MCP logs show: `Tool called: get_customer_orders` with `customer_id=101`
   - [ ] No errors in CRM MCP logs

5. **Verify expected data in response:**
   - [ ] Chat response includes order number `1001`
   - [ ] Chat response includes status: `shipped`
   - [ ] Chat response includes tracking number: `TRK-29481`
   - [ ] Chat response includes estimated delivery date
   - [ ] Response is in natural language (not raw JSON)

6. **Verify no hallucination:**
   - [ ] Order 1001 exists in seed data (check CSV: `data/seed/orders.csv`)
   - [ ] Tracking number `TRK-29481` matches seed data exactly
   - [ ] Delivery date is reasonable (not 2025 or 2099)

### Pass/Fail Criteria

**PASS:** Response includes Order 1001, tracking TRK-29481, shipped status, and delivery estimate in natural language.  
**FAIL:** Response contains wrong order ID, wrong tracking number, hallucinated data, or errors.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 2: "These boots don't fit"

**Customer:** James Chen (ID: 102)  
**User Query:** "I got my hiking boots but they're too small. Can I return them?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `search_knowledge_base` (return policy + sizing guide)

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 102 (James Chen)
   - [ ] Verify customer switched

2. **Send query to chat:**
   - [ ] Type: "I got my hiking boots but they're too small. Can I return them?"
   - [ ] Press Enter
   - [ ] Record response time: _____ ms

3. **Verify Orchestrator routing:**
   - [ ] Check Orchestrator logs: should route to CRM Agent
   - [ ] CRM Agent initialization logged

4. **Verify MCP tool calls:**
   - [ ] CRM MCP logs show: `Tool called: get_customer_orders` with `customer_id=102`
   - [ ] Knowledge MCP logs show: `search_knowledge_base` query contains keywords like "return", "policy", or "sizing"
   - [ ] No errors in either MCP server logs

5. **Verify expected data in response:**
   - [ ] Response mentions Order 1002
   - [ ] Response mentions product: "TrailBlazer Hiking Boots"
   - [ ] Response confirms order was delivered
   - [ ] Response mentions the 30-day return window
   - [ ] Response references boot sizing guide (or similar)
   - [ ] Response explains return process

6. **Verify knowledge base integration:**
   - [ ] Response contains information from return policy document
   - [ ] Response contains sizing guidance (not hallucinated)

### Pass/Fail Criteria

**PASS:** Response includes Order 1002, product name, return policy confirmation, sizing guide reference, and return process explanation.  
**FAIL:** Missing order details, wrong product name, no return policy mention, or hallucinated sizing data.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 3: "Any deals on tents?"

**Customer:** Sarah Johnson (ID: 103)  
**User Query:** "I'm a Gold member — are there any tent deals right now?"  
**Expected Agent Route:** Orchestrator → Product Agent  
**Expected MCP Tools:** `get_eligible_promotions`, `get_products` (tent category)

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 103 (Sarah Johnson)
   - [ ] Verify customer switched

2. **Send query to chat:**
   - [ ] Type: "I'm a Gold member — are there any tent deals right now?"
   - [ ] Press Enter
   - [ ] Record response time: _____ ms

3. **Verify Orchestrator routing:**
   - [ ] Check Orchestrator logs: should route to Product Agent
   - [ ] Product Agent initialization logged

4. **Verify MCP tool calls:**
   - [ ] CRM MCP logs show: `Tool called: get_eligible_promotions` with `customer_id=103`
   - [ ] CRM MCP logs show: `Tool called: get_products` with `category=tents` or similar
   - [ ] No errors in CRM MCP logs

5. **Verify expected data in response:**
   - [ ] Response confirms Sarah's loyalty tier: "Gold"
   - [ ] Response mentions promotion: "Spring Camping Sale" or similar
   - [ ] Response mentions discount: "15% off" or similar percentage
   - [ ] Response lists tent products with names and prices
   - [ ] Response includes product image references (filename format: `*.png` or `*.jpg`)
   - [ ] Response confirms Sarah qualifies for promotion

6. **Verify product data:**
   - [ ] Tent products shown are from seed data (`data/seed/products.csv`)
   - [ ] Prices are realistic (not 0 or 999999)
   - [ ] Image filenames match product catalog structure

### Pass/Fail Criteria

**PASS:** Response confirms Gold tier, lists "Spring Camping Sale" promotion, shows 15% discount, lists ≥2 tent products with images, and confirms eligibility.  
**FAIL:** Wrong loyalty tier, missing promotion, wrong discount, no products listed, or hallucinated product names.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 4: "My jacket arrived damaged"

**Customer:** Michael Brown (ID: 104)  
**User Query:** "The jacket I ordered arrived with a tear in the sleeve. What can I do?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `search_knowledge_base` (warranty policy), `create_support_ticket`

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 104 (Michael Brown)
   - [ ] Verify customer switched

2. **Send query to chat:**
   - [ ] Type: "The jacket I ordered arrived with a tear in the sleeve. What can I do?"
   - [ ] Press Enter
   - [ ] Record response time: _____ ms

3. **Verify Orchestrator routing:**
   - [ ] Check Orchestrator logs: should route to CRM Agent
   - [ ] CRM Agent initialization logged

4. **Verify MCP tool calls:**
   - [ ] CRM MCP logs show: `Tool called: get_customer_orders` with `customer_id=104`
   - [ ] Knowledge MCP logs show: `search_knowledge_base` query contains keywords like "warranty" or "defect"
   - [ ] CRM MCP logs show: `Tool called: create_support_ticket` with `category=product-issue`, `priority=high`
   - [ ] No errors in any MCP logs

5. **Verify expected data in response:**
   - [ ] Response mentions Order 1004
   - [ ] Response mentions product: "Alpine Summit Jacket"
   - [ ] Response confirms order was delivered
   - [ ] Response mentions warranty policy coverage for manufacturing defects
   - [ ] Response explains replacement or refund options
   - [ ] Response confirms support ticket was created
   - [ ] Response provides ticket reference number (if available)

6. **Verify support ticket creation:**
   - [ ] CRM API logs show ticket creation endpoint hit
   - [ ] Query CRM API: `curl http://localhost:5001/api/v1/tickets?customer_id=104` → returns ticket with status `open`
   - [ ] Ticket contains: customer_id=104, category=product-issue, priority=high

### Pass/Fail Criteria

**PASS:** Response includes Order 1004, product name, warranty policy reference, options for replacement/refund, and confirmation of ticket creation with ticket ID.  
**FAIL:** Missing order details, wrong product, no warranty mention, no ticket created, or missing ticket ID in response.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 5: "Help me pick a backpack"

**Customer:** Lisa Anderson (ID: 105)  
**User Query:** "I'm planning a 5-day backpacking trip. What backpack should I get?"  
**Expected Agent Route:** Orchestrator → Product Agent  
**Expected MCP Tools:** `search_knowledge_base` (backpack fitting guide), `get_products` (backpack category)

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 105 (Lisa Anderson)
   - [ ] Verify customer switched

2. **Send query to chat:**
   - [ ] Type: "I'm planning a 5-day backpacking trip. What backpack should I get?"
   - [ ] Press Enter
   - [ ] Record response time: _____ ms

3. **Verify Orchestrator routing:**
   - [ ] Check Orchestrator logs: should route to Product Agent
   - [ ] Product Agent initialization logged

4. **Verify MCP tool calls:**
   - [ ] Knowledge MCP logs show: `search_knowledge_base` query contains keywords like "backpack", "fitting", or "capacity"
   - [ ] CRM MCP logs show: `Tool called: get_products` with `category=backpacks` or similar
   - [ ] No errors in any MCP logs

5. **Verify expected data in response:**
   - [ ] Response references backpack fitting guide (or knowledge base search result)
   - [ ] Response recommends specific capacity range (e.g., "60-75L")
   - [ ] Response mentions torso measurement and hip belt adjustment
   - [ ] Response lists ≥2 backpack products with names and prices
   - [ ] Response includes product image references (filename format)
   - [ ] Response explains capacity recommendation for 5-day trip

6. **Verify knowledge base and product integration:**
   - [ ] Capacity recommendation (60-75L) matches seed knowledge base (not hallucinated)
   - [ ] Products listed are from seed data with correct names and prices
   - [ ] Image filenames reference backpack images

### Pass/Fail Criteria

**PASS:** Response includes backpack fitting guide, recommends 60-75L capacity, mentions torso/hip measurement, lists ≥2 backpacks with images, and explains 5-day justification.  
**FAIL:** Wrong capacity recommendation, no fitting guide mention, no products listed, missing images, or hallucinated specs.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 6: "How do I waterproof my tent?"

**Customer:** Tom Garcia (ID: 106)  
**User Query:** "I just got my tent delivered. How should I take care of it?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `search_knowledge_base` (gear care guide)

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 106 (Tom Garcia)
   - [ ] Verify customer switched

2. **Send query to chat:**
   - [ ] Type: "I just got my tent delivered. How should I take care of it?"
   - [ ] Press Enter
   - [ ] Record response time: _____ ms

3. **Verify Orchestrator routing:**
   - [ ] Check Orchestrator logs: should route to CRM Agent
   - [ ] CRM Agent initialization logged

4. **Verify MCP tool calls:**
   - [ ] CRM MCP logs show: `Tool called: get_customer_orders` with `customer_id=106`
   - [ ] Knowledge MCP logs show: `search_knowledge_base` query contains keywords like "care", "maintenance", "waterproof"
   - [ ] No errors in any MCP logs

5. **Verify expected data in response:**
   - [ ] Response confirms Order 1006
   - [ ] Response confirms product: "Basecamp 4P Tent"
   - [ ] Response confirms order was delivered
   - [ ] Response includes care recommendations: cleaning, waterproofing, storage
   - [ ] Response references gear care guide (from knowledge base)
   - [ ] Recommendations are specific (not generic tent advice)

6. **Verify knowledge base integration:**
   - [ ] Care recommendations come from knowledge base (not hallucinated)
   - [ ] Response includes actionable steps (e.g., "use silicone-based sealant", "store in dry location")

### Pass/Fail Criteria

**PASS:** Response includes Order 1006, product name, gear care guide reference, and specific care steps (cleaning, waterproofing, storage).  
**FAIL:** Wrong order, missing product name, no care guide mention, generic recommendations, or hallucinated specs.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 7: "I want to cancel my order"

**Customer:** Rachel Kim (ID: 107)  
**User Query:** "I changed my mind about my order. Can I cancel it?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `create_support_ticket`

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 107 (Rachel Kim)
   - [ ] Verify customer switched

2. **Send query to chat:**
   - [ ] Type: "I changed my mind about my order. Can I cancel it?"
   - [ ] Press Enter
   - [ ] Record response time: _____ ms

3. **Verify Orchestrator routing:**
   - [ ] Check Orchestrator logs: should route to CRM Agent
   - [ ] CRM Agent initialization logged

4. **Verify MCP tool calls:**
   - [ ] CRM MCP logs show: `Tool called: get_customer_orders` with `customer_id=107`
   - [ ] CRM MCP logs show: `Tool called: create_support_ticket` with `category=general`, `priority=medium`
   - [ ] No errors in CRM MCP logs

5. **Verify expected data in response:**
   - [ ] Response mentions Order 1007
   - [ ] Response confirms order status: "processing"
   - [ ] Response confirms cancellation is possible before shipping
   - [ ] Response confirms support ticket was created for cancellation request
   - [ ] Response provides ticket reference number (if available)
   - [ ] Response explains next steps (e.g., "we'll process this within 24 hours")

6. **Verify support ticket creation:**
   - [ ] CRM API logs show ticket creation endpoint hit
   - [ ] Query CRM API: `curl http://localhost:5001/api/v1/tickets?customer_id=107` → returns ticket with status `open`
   - [ ] Ticket contains: customer_id=107, category=general, priority=medium

### Pass/Fail Criteria

**PASS:** Response includes Order 1007, confirms processing status, explains cancellation is possible, creates ticket, and provides ticket ID.  
**FAIL:** Wrong order, wrong status, no cancellation confirmation, ticket not created, or missing ticket ID.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 8: "What happened to my refund?"

**Customer:** David Lee (ID: 108)  
**User Query:** "I returned some items a while ago but haven't gotten my refund yet."  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `get_support_tickets`, `search_knowledge_base` (return policy)

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 108 (David Lee)
   - [ ] Verify customer switched

2. **Send query to chat:**
   - [ ] Type: "I returned some items a while ago but haven't gotten my refund yet."
   - [ ] Press Enter
   - [ ] Record response time: _____ ms

3. **Verify Orchestrator routing:**
   - [ ] Check Orchestrator logs: should route to CRM Agent
   - [ ] CRM Agent initialization logged

4. **Verify MCP tool calls:**
   - [ ] CRM MCP logs show: `Tool called: get_customer_orders` with `customer_id=108`
   - [ ] CRM MCP logs show: `Tool called: get_support_tickets` with `customer_id=108`
   - [ ] Knowledge MCP logs show: `search_knowledge_base` query contains keywords like "return", "refund", "timeline"
   - [ ] No errors in any MCP logs

5. **Verify expected data in response:**
   - [ ] Response mentions Order 1008
   - [ ] Response confirms order status: "returned"
   - [ ] Response mentions existing support ticket (if present in seed data)
   - [ ] Response references return policy refund timeline: "5-10 business days"
   - [ ] Response provides status update based on ticket (if available)
   - [ ] Response includes escalation path or next steps

6. **Verify knowledge base and ticket integration:**
   - [ ] Refund timeline (5-10 business days) matches knowledge base (not hallucinated)
   - [ ] Support ticket information is accurate (matches CRM API query)
   - [ ] Response is empathetic and actionable

### Pass/Fail Criteria

**PASS:** Response includes Order 1008, confirms returned status, references support ticket, states 5-10 business day refund timeline, and provides status update.  
**FAIL:** Wrong order, wrong status, no ticket mention, hallucinated timeline, or missing refund policy reference.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Multi-Intent Verification (Advanced)

**Optional test:** If you wish to verify Orchestrator's ability to handle multi-intent queries (Scenario 8 in business-scenario.md mentions this):

**Customer:** Any customer (recommend 101 or 102)  
**User Query:** "I have two questions: Where's my order and do you have any backpack deals?"  
**Expected Behavior:** Orchestrator should either:
- (a) Handle both intents in one response, routing intelligently to CRM and Product agents in sequence, OR
- (b) Ask clarifying question: "I can help with both! Let me start with your order status..."

### Test Steps

1. **Select any customer (recommend 101)**
2. **Send multi-intent query:**
   - [ ] Type: "I have two questions: Where's my order and do you have any backpack deals?"
   - [ ] Press Enter

3. **Verify Orchestrator logic:**
   - [ ] Check Orchestrator logs for intent detection logic
   - [ ] Verify both agents were invoked (if single-turn) or clarification was offered (if multi-turn)

4. **Verify response quality:**
   - [ ] Response addresses both intents or provides clear clarification
   - [ ] Response does not hallucinate data
   - [ ] Response is coherent and actionable

**Note:** This is an advanced scenario. It's acceptable for the system to handle one intent at a time on first release.

---

## Summary Results Sheet

### Run Date: _______________  
### Tester: _______________  
### Environment: LOCAL DEV (localhost:5000-5008)

| Scenario | Title | Result | Notes |
|----------|-------|--------|-------|
| 1 | Where's my order? (Emma Wilson) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 2 | Boots don't fit (James Chen) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 3 | Tent deals (Sarah Johnson) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 4 | Jacket damaged (Michael Brown) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 5 | Backpack recommendation (Lisa Anderson) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 6 | Tent care (Tom Garcia) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 7 | Cancel order (Rachel Kim) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 8 | Refund status (David Lee) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| **TOTAL** | | **___ / 8 PASS** | |

### Smoke Tests

| Check | Result | Notes |
|-------|--------|-------|
| All services healthy | ☐ PASS ☐ FAIL | |
| CRM API /health → 200 | ☐ PASS ☐ FAIL | |
| BFF /health → 200 | ☐ PASS ☐ FAIL | |
| Customers endpoint responsive | ☐ PASS ☐ FAIL | |
| MCP servers connected | ☐ PASS ☐ FAIL | |
| Auth dropdown populated | ☐ PASS ☐ FAIL | |

---

## Troubleshooting Guide

### Service Won't Start

- [ ] Check `dotnet run --project src/AppHost` output for errors
- [ ] Verify all ports (5001–5008, 15000) are available
- [ ] Check `.squad/secrets/*` files exist and are populated
- [ ] Run `setup-local.sh` to re-initialize environment

### No customers in response

- [ ] Verify `data/seed/customers.csv` exists and has 8+ rows
- [ ] Check `curl http://localhost:5001/api/v1/customers` returns JSON
- [ ] Verify CRM API logs show CSV loading on startup
- [ ] Re-run seed-data project if needed

### MCP servers not connecting

- [ ] Check Orchestrator logs for "CRM MCP: connected" message
- [ ] Verify CRM MCP is running on port 5002
- [ ] Verify Knowledge MCP is running on port 5003
- [ ] Restart AppHost and check connection logs

### Chat response is slow or times out

- [ ] Check Azure OpenAI API key in `.squad/secrets/foundry.json`
- [ ] Verify network connectivity to Azure OpenAI endpoint
- [ ] Check BFF API logs for timeout errors
- [ ] Verify Orchestrator agent is running (check logs)

### Hallucinated data in response

- [ ] Verify agent system prompt in source code matches specification
- [ ] Check that MCP tools are being called (not agent making up data)
- [ ] Review knowledge base documents for accuracy
- [ ] File issue if agent is inventing order numbers or customer data

---

## Test Execution Log

```
Session Start: _______________
Tester: _______________
Environment: _______________

[Time] Scenario 1: _______________
[Time] Scenario 2: _______________
[Time] Scenario 3: _______________
[Time] Scenario 4: _______________
[Time] Scenario 5: _______________
[Time] Scenario 6: _______________
[Time] Scenario 7: _______________
[Time] Scenario 8: _______________

Session End: _______________
Total Runtime: _______________
```

---

## Appendix: Data Reference

### Customer IDs and Names (from seed data)

| ID | Name | Loyalty Tier |
|----|------|--------------|
| 101 | Emma Wilson | Silver |
| 102 | James Chen | Bronze |
| 103 | Sarah Johnson | Gold |
| 104 | Michael Brown | Silver |
| 105 | Lisa Anderson | Platinum |
| 106 | Tom Garcia | Bronze |
| 107 | Rachel Kim | Silver |
| 108 | David Lee | Gold |

### Order Reference (from seed data)

| Order ID | Customer ID | Product | Status | Tracking | Reference |
|----------|-------------|---------|--------|----------|-----------|
| 1001 | 101 | (Unspecified) | shipped | TRK-29481 | Scenario 1 |
| 1002 | 102 | TrailBlazer Hiking Boots | delivered | (TBD) | Scenario 2 |
| 1003 | 103 | (Tent - from promotions) | (TBD) | (TBD) | Scenario 3 |
| 1004 | 104 | Alpine Summit Jacket | delivered | (TBD) | Scenario 4 |
| 1005 | 105 | (Backpack - from recommendations) | (TBD) | (TBD) | Scenario 5 |
| 1006 | 106 | Basecamp 4P Tent | delivered | (TBD) | Scenario 6 |
| 1007 | 107 | (Unspecified) | processing | (TBD) | Scenario 7 |
| 1008 | 108 | (Returned items) | returned | (TBD) | Scenario 8 |

### Service Ports Reference

| Service | Port | Health Endpoint | Purpose |
|---------|------|-----------------|---------|
| CRM API | 5001 | `/api/v1/health` | Cosmos DB backend for customers, orders, tickets |
| CRM MCP | 5002 | (none) | Tool server wrapping CRM API |
| Knowledge MCP | 5003 | (none) | Vector search on policies and guides |
| CRM Agent | 5004 | (none) | Agent with CRM tools |
| Product Agent | 5005 | (none) | Agent with product and knowledge tools |
| Orchestrator Agent | 5006 | (none) | Router to specialist agents |
| BFF API | 5007 | `/health` | Frontend backend, auth, chat orchestration |
| Blazor UI | 5008 | (none) | WASM front-end with customer auth dropdown |
| Aspire Dashboard | 15000 | (web UI) | Service health and logs |

---

## Revision History

| Date | Tester | Status | Notes |
|------|--------|--------|-------|
| 2026-03-24 | Peter | Draft | Initial E2E verification checklist created mapping all 8 scenarios to testable steps |

---

**Created by:** Peter (Tester)  
**Purpose:** Enable reproducible, systematic validation of all 8 customer scenarios using the local dev stack  
**Owner:** Almir Banjanovic (Project Lead)  
**Next Steps:** Run smoke tests, execute scenarios 1–8, record results, file issues for any failures
