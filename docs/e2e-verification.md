# E2E Verification Checklist: 8 Customer Scenarios

This document maps all 8 business scenarios to executable test steps using the local dev stack.

**Last Updated:** 2026-05-12  
**Tester:** Peter  
**Status:** Ready for validation

---

## Prerequisites

Before running any scenario:

1. **Local dev stack is running:**
   - [ ] `dotnet run --project src/AppHost` is executing in terminal
   - [ ] All 9 services are healthy and ready

2. **Service endpoints are responding:**
   - [ ] Aspire Dashboard: https://localhost:15888 → shows all 9 services in "Running" state
   - [ ] Blazor UI: http://localhost:5008 → loads without auth errors
   - [ ] BFF API health: `curl http://localhost:5007/health` → 200 OK
   - [ ] CRM API health: `curl http://localhost:5001/health` → 200 OK
   - [ ] Fraud Workflow health: `curl http://localhost:5010/health` → 200 OK

3. **Seed data is loaded:**
   - [ ] `curl http://localhost:5001/api/v1/customers` → returns all 8+ customers (JSON array, 200 OK)
   - [ ] `curl http://localhost:5001/api/v1/customers/101` → Emma Wilson profile present
   - [ ] All 8 customer IDs (101–108) exist in the response

4. **MCP servers are connected:**
   - [ ] Orchestrator dashboard logs show "CRM MCP: connected" on startup
   - [ ] Orchestrator dashboard logs show "Knowledge MCP: connected" on startup

5. **UI is authenticated and ready:**
   - [ ] Navigate to http://localhost:5008
   - [ ] First load redirects to `login.microsoftonline.com`
   - [ ] Sign in as `emma.wilson-local@<your-tenant>` (UPN + password in `local-dev-credentials.txt` at the repo root)
   - [ ] User badge shows "Emma Wilson" — BFF mapped UPN → customer 101
   - [ ] Chat input box is active and focused

---

## Smoke Test Checklist

Run these checks before executing scenarios. Record results below.

```
Date: _______________
Tester: _______________

[ ] All services healthy in Aspire Dashboard (15888)
[ ] Blazor UI loads without 401/403 errors
[ ] BFF /health returns 200
[ ] CRM API /health returns 200
[ ] CRM API /api/v1/customers/101 returns 200 with the seeded customer profile
[ ] Blazor UI redirects to login.microsoftonline.com on first visit
[ ] Sign-in as `emma.wilson-local@<your-tenant>` succeeds and resolves to customer 101
[ ] Chat input box is focusable and ready
[ ] Orchestrator logs show both MCPs connected
[ ] Knowledge base index is online (Product Agent startup logs confirm)
[ ] System ready for E2E testing
[ ] MCP transport failure recovery test passed (client reconnect path)
[ ] Identity propagation check passed (`X-Customer-Entra-Id` preserved end-to-end)
[ ] Fraud workflow checkpoint/resume test passed after service restart
[ ] Idempotency check passed for duplicate side-effect requests
```

---

## Scenario 1: "Where's my order?"

**Customer:** Emma Wilson (ID: 101)  
**User Query:** "I placed an order a few days ago — can you tell me where it is?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders` → Order detail extraction

### Test Steps

1. **Sign in as the customer:**
   - [ ] Navigate to http://localhost:5008 (use an incognito window for clean state)
   - [ ] Sign in as `emma.wilson-local@<your-tenant>`
   - [ ] User badge shows "Emma Wilson" — BFF resolved UPN → customer 101

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
   - [ ] Chat response includes status: `delivered` (the order was delivered on 2026-03-08 — well outside the 30-day return window)
   - [ ] Chat response includes tracking number: `TRK-29481`
   - [ ] Chat response includes the delivery date (2026-03-08)
   - [ ] Response is in natural language (not raw JSON)

6. **Verify no hallucination:**
   - [ ] Order 1001 exists in seed data (check CSV: `data/contoso-crm/orders.csv`)
   - [ ] Tracking number `TRK-29481` matches seed data exactly
   - [ ] Delivery date matches the seed (2026-03-08)

### Pass/Fail Criteria

**PASS:** Response includes Order 1001, tracking TRK-29481, delivered status, and the 2026-03-08 delivery date in natural language.  
**FAIL:** Response contains wrong order ID, wrong tracking number, hallucinated data, or errors.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 2: "These boots don't fit"

**Customer:** James Chen (ID: 102)  
**User Query:** "I got my hiking boots but they're too small. Can I return them?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `get_support_tickets` (find ST-003), `search_knowledge_base` (sizing guide)  
**Seed plot point:** Order 1002 is already in `return-started` and ticket **ST-003** (category=`return`, status=`open`) is active with a live UPS return label `LBL-seed1002`. The right behavior is to surface the *existing* return rather than open a new one. If the customer asks to cancel the return, the agent should call `cancel_support_ticket` — the API voids the label first and reverts the order to `delivered`.

### Test Steps

1. **Sign in as the customer (incognito or new browser profile):**
   - [ ] Sign in as `james.chen-local@<your-tenant>` (customer 102)
   - [ ] User badge confirms James Chen

2. **Send query to chat:**
   - [ ] Type: "I got my hiking boots but they're too small. Can I return them?"
   - [ ] Press Enter
   - [ ] Record response time: _____ ms

3. **Verify Orchestrator routing:**
   - [ ] Check Orchestrator logs: should route to CRM Agent
   - [ ] CRM Agent initialization logged

4. **Verify MCP tool calls:**
   - [ ] CRM MCP logs show: `Tool called: get_customer_orders` with `customer_id=102`
   - [ ] CRM MCP logs show: `Tool called: get_support_tickets` with `customer_id=102` (to find ST-003)
   - [ ] CRM MCP logs show **no** call to `create_support_ticket` (return is already open — don't open a duplicate)
   - [ ] Knowledge MCP logs may show `search_knowledge_base` for the sizing guide
   - [ ] No errors in either MCP server logs

5. **Verify expected data in response:**
   - [ ] Response mentions Order 1002
   - [ ] Response mentions product: "TrailBlazer Hiking Boots"
   - [ ] Response acknowledges the order is already in a return (status `return-started`)
   - [ ] Response surfaces ticket **ST-003** as the existing return
   - [ ] Response mentions the active prepaid return label (`LBL-seed1002`, UPS) and links to it or to the carrier
   - [ ] Response offers next steps: drop the package at UPS, or cancel the return if the customer changed their mind, or get sizing help for a fresh order

6. **(Optional) Verify cancel-side path:**
   - [ ] Follow up: "Actually, never mind — cancel the return."
   - [ ] CRM MCP logs show: `Tool called: cancel_support_ticket` with `ticket_id=ST-003`
   - [ ] CRM API logs show the label was voided **before** the ticket was cancelled
   - [ ] Query CRM API: `curl http://localhost:5001/api/v1/orders/1002` → status reverts from `return-started` to `delivered`
   - [ ] Query CRM API: `curl http://localhost:5001/api/v1/tickets/ST-003` → `status=cancelled`, `return_label_status=voided`, `return_label_voided_at` populated

### Pass/Fail Criteria

**PASS:** Response includes Order 1002, product name, surfaces the existing ST-003 ticket and `LBL-seed1002` UPS label, and does **not** create a duplicate ticket. (Optional cancel path: label voided first, ticket cancelled, order reverted to `delivered`.)  
**FAIL:** Agent calls `create_support_ticket` (duplicate return), misses ST-003 / the active label, claims the order is still "delivered, can return" without acknowledging the open return, or the cancel path leaves the order in `return-started`.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 3: "Any deals on tents?"

**Customer:** Sarah Miller (ID: 103)  
**User Query:** "I'm a Gold member — are there any tent deals right now?"  
**Expected Agent Route:** Orchestrator → Product Agent  
**Expected MCP Tools:** `get_eligible_promotions`, `get_products` (tent category)

### Test Steps

1. **Sign in as the customer (incognito or new browser profile):**
   - [ ] Sign in as `sarah.miller-local@<your-tenant>` (customer 103, Gold tier)
   - [ ] User badge confirms Sarah Miller

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
   - [ ] Tent products shown are from seed data (`data/contoso-crm/products.csv`)
   - [ ] Prices are realistic (not 0 or 999999)
   - [ ] Image filenames match product catalog structure

### Pass/Fail Criteria

**PASS:** Response confirms Gold tier, lists "Spring Camping Sale" promotion, shows 15% discount, lists ≥2 tent products with images, and confirms eligibility.  
**FAIL:** Wrong loyalty tier, missing promotion, wrong discount, no products listed, or hallucinated product names.

**Result:** ☐ PASS | ☐ FAIL | ☐ BLOCKED  
**Notes:** _______________________________________________________________

---

## Scenario 4: "My jacket arrived damaged"

**Customer:** David Park (ID: 104)  
**User Query:** "The jacket I ordered arrived with a tear in the sleeve. What can I do?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `search_knowledge_base` (warranty policy), `create_support_ticket`

### Test Steps

1. **Sign in as the customer (incognito or new browser profile):**
   - [ ] Sign in as `david.park-local@<your-tenant>` (customer 104)
   - [ ] User badge confirms David Park

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

**Customer:** Lisa Torres (ID: 105)  
**User Query:** "I'm planning a 5-day backpacking trip. What backpack should I get?"  
**Expected Agent Route:** Orchestrator → Product Agent  
**Expected MCP Tools:** `search_knowledge_base` (backpack fitting guide), `get_products` (backpack category)

### Test Steps

1. **Sign in as the customer (incognito or new browser profile):**
   - [ ] Sign in as `lisa.torres-local@<your-tenant>` (customer 105)
   - [ ] User badge confirms Lisa Torres

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

**Customer:** Mike Johnson (ID: 106)  
**User Query:** "I just got my tent delivered. How should I take care of it?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `search_knowledge_base` (gear care guide)

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 106 (Mike Johnson)
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

**Customer:** Anna Roberts (ID: 107)  
**User Query:** "I changed my mind about my order. Can I cancel it?"  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `create_support_ticket`

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 107 (Anna Roberts)
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

**Customer:** Tom Garcia (ID: 108)  
**User Query:** "I returned some items a while ago but haven't gotten my refund yet."  
**Expected Agent Route:** Orchestrator → CRM Agent  
**Expected MCP Tools:** `get_customer_orders`, `get_support_tickets`, `search_knowledge_base` (return policy)

### Test Steps

1. **Select customer in Blazor UI:**
   - [ ] Dev auth dropdown → select ID 108 (Tom Garcia)
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
### Environment: LOCAL DEV (localhost:5001-5008)

| Scenario | Title | Result | Notes |
|----------|-------|--------|-------|
| 1 | Where's my order? (Emma Wilson) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 2 | Boots don't fit (James Chen) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 3 | Tent deals (Sarah Miller) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 4 | Jacket damaged (David Park) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 5 | Backpack recommendation (Lisa Torres) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 6 | Tent care (Mike Johnson) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 7 | Cancel order (Anna Roberts) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
| 8 | Refund status (Tom Garcia) | ☐ PASS ☐ FAIL ☐ BLOCKED | |
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
- [ ] Verify all ports (5001–5008, 15888) are available
- [ ] Check `.squad/secrets/*` files exist and are populated
- [ ] Run `setup-local.sh` to re-initialize environment

### No customers in response

- [ ] Verify `data/contoso-crm/customers.csv` exists and has 8+ rows
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
| 101 | Emma Wilson  | Silver |
| 102 | James Chen   | Bronze |
| 103 | Sarah Miller | Gold |
| 104 | David Park   | Silver |
| 105 | Lisa Torres  | Bronze |
| 106 | Mike Johnson | Gold |
| 107 | Anna Roberts | Bronze |
| 108 | Tom Garcia   | Silver |

### Order Reference (from seed data, May 2026 demo)

| Order ID | Customer ID | Product | Status | Tracking | Order → Delivery | Reference |
|----------|-------------|---------|--------|----------|------------------|-----------|
| 1001 | 101 (Emma) | (Unspecified) | delivered | TRK-29481 | 2026-03-01 → 2026-03-08 (out-of-window demo) | Scenario 1 |
| 1002 | 102 (James) | TrailBlazer Hiking Boots | return-started | TRK-28734 | 2026-04-15 → 2026-04-21 (return ST-003 already open with active label `LBL-seed1002`) | Scenario 2 |
| 1003 | 103 (Sarah) | Basecamp 4P Tent | delivered | TRK-28190 | 2026-04-25 → 2026-05-01 (within window) | Scenario 3 / Lab 3 plot |
| 1004 | 104 (David) | Alpine Summit Jacket | delivered | TRK-27956 | 2026-04-18 → 2026-04-24 | Scenario 4 |
| 1005 | 105 (Lisa) | (Backpack — from recommendations) | processing | — | 2026-05-08 → — | Scenario 5 |
| 1006 | 106 (Mike) | Basecamp 4P Tent | delivered | TRK-28301 | 2026-04-22 → 2026-04-28 | Scenario 6 |
| 1007 | 107 (Anna) | (Unspecified) | processing | — | 2026-05-09 → — | Scenario 7 |
| 1008 | 108 (Tom) | (Returned items) | returned | TRK-25890 | 2026-01-15 → 2026-01-21 | Scenario 8 |
| 1009 | 109 (Rachel) | (Multi-line) | shipped | TRK-30150 | 2026-05-10 → EDD 2026-05-15 | Active in-flight order (Rachel is *not* a local test user; visible in the BFF as a non-mapped customer) |
| 1010 | 110 (Carlos) | (Recently delivered) | delivered | TRK-29870 | 2026-04-14 → 2026-04-21 | Within return window (Carlos is *not* a local test user) |
| 1011 | 103 (Sarah) | (Recently delivered) | delivered | TRK-29915 | 2026-04-22 → 2026-04-28 | Sarah's second order — within window |
| 1012 | 106 (Mike) | (Older delivered) | delivered | TRK-25650 | 2026-01-20 → 2026-01-26 (second out-of-window demo) | Bonus return-window plot |

### Service Ports Reference

| Service | Port | Health Endpoint | Purpose |
|---------|------|-----------------|---------|
| CRM API | 5001 | `/health` | Cosmos DB backend for customers, orders, tickets |
| CRM MCP | 5002 | (none) | Tool server wrapping CRM API |
| Knowledge MCP | 5003 | (none) | Vector search on policies and guides |
| CRM Agent | 5004 | (none) | Agent with CRM tools |
| Product Agent | 5005 | (none) | Agent with product and knowledge tools |
| Orchestrator Agent | 5006 | (none) | Router to specialist agents |
| BFF API | 5007 | `/health` | Frontend backend, auth, chat orchestration |
| Blazor UI | 5008 | (none) | WASM front-end with customer auth dropdown |
| Fraud Workflow | 5010 | `/health` | Refund-risk workflow (fan-out, aggregator, paused human gate) |
| Aspire Dashboard | 15888 | (web UI) | Service health and logs |

---

## Revision History

| Date | Tester | Status | Notes |
|------|--------|--------|-------|
| 2026-03-24 | Peter | Draft | Initial E2E verification checklist created mapping all 8 scenarios to testable steps |
| 2026-05-12 | Peter | Refresh | Updated for the May 2026 seed bump and the return-flow rewrite — Order 1001 expectation flipped from `shipped` to `delivered` (out-of-window demo), Scenario 2 (James) now exercises an open `return-started` ticket with a live `LBL-seed1002` label, customer-name table corrected to match the actual seed data, fraud-workflow added to the service inventory. |

---

**Created by:** Peter (Tester)  
**Purpose:** Enable reproducible, systematic validation of all 8 customer scenarios using the local dev stack  
**Owner:** Almir Banjanovic (Project Lead)  
**Next Steps:** Run smoke tests, execute scenarios 1–8, record results, file issues for any failures
