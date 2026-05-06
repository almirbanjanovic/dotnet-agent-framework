# Lab 1 — Foundry-only Deployment (Local Track)

> **Track:** Local — Foundry only, everything else runs on your laptop.
> Looking for the Full Azure Track instead? See [`../full-azure/lab-1.md`](../full-azure/lab-1.md).

The Local Track provisions **only Azure AI Foundry** (one resource group, one AI Services account, two model deployments) and runs everything else on your laptop via .NET Aspire with in-memory data services.

## Prerequisites

- [Lab 0](lab-0.md) completed (`az login` and tools installed).
- Your `az` session is in the subscription where you want the Foundry account.

## Step 1 — Provision Foundry and generate appsettings

From the repository root:

```powershell
# PowerShell
./infra/setup-local.ps1
```

```bash
# Bash / WSL / macOS
chmod +x infra/setup-local.sh
./infra/setup-local.sh
```

The script:

1. Creates the working resource group `rg-dotnetagent-localdev` out-of-band via `az group create` (idempotent; not Terraform-managed, so it survives `setup-local -Cleanup`).
2. Bootstraps a Standard_LRS storage account + `tfstate` container in the working RG, grants you `Storage Blob Data Contributor`, generates `infra/terraform/local-dev/backend.hcl` (gitignored), and runs `terraform init -reconfigure -backend-config=backend.hcl`. Terraform state is stored **remotely** in that storage account so it survives `setup-local -Cleanup` (the storage account isn't in TF state and `terraform destroy` won't touch it). To wipe state for real, run `az group delete --name rg-dotnetagent-localdev`.
3. Runs `terraform apply` in `infra/terraform/local-dev/` to create inside the working RG:
   - 1 Azure AI Foundry account with a default project (`default-project`)
   - 2 model deployments: chat (`gpt-4.1`) + embeddings (`text-embedding-3-small`)
   - Grants you `Cognitive Services OpenAI User` on the account and `Azure AI User` on the project
   - **Microsoft Entra:** a SPA app registration (`app-dotnetagent-local-bff-localdev`) with `http://localhost:5008/authentication/login-callback` redirect URIs, the `Customer` app role, plus 8 test users (Emma Wilson, James Chen, Sarah Miller, David Park, Lisa Torres, Mike Johnson, Anna Roberts, Tom Garcia) with the role assigned
4. Reads the Foundry project endpoint, deployment names, tenant ID, BFF client ID, and customer-map JSON from Terraform output.
5. Renders each `src/<component>/appsettings.Local.json.template` into a per-component `appsettings.Local.json`. Auth everywhere is `DefaultAzureCredential` to Azure resources; user sign-in to the Blazor UI is real MSAL/Entra.
6. Writes each test user's UPN and generated password to `local-dev-credentials.txt` at the repo root — gitignored. Passwords **persist across runs** (Terraform owns the users and the random password resources stay stable in state); they only change after `setup-local -Cleanup` followed by a fresh setup.

> ### 🔑 Where the test-user passwords live
>
> When `setup-local` finishes it writes all 8 test-user UPNs and their
> passwords to a single file at the **repo root**:
>
> ```text
> local-dev-credentials.txt
> ```
>
> **Open this file** — it has every UPN you'll need to sign in to the Blazor UI in this
> lab and in Labs 2 / 3. Example contents:
>
> ```text
> # Local-dev test-user credentials
> # Generated: 2026-05-05 17:50:21 -07:00
> # Tenant:    7960be14-fc91-4f30-8ca1-237851909103
> # WARNING:   gitignored — do not commit. Passwords persist across setup-local runs;
> #            they only change after a -Cleanup followed by a fresh setup.
>
> key      upn                                                 password
> ---      ---                                                 ---
> anna     anna.roberts-local@<your-tenant>.onmicrosoft.com    Contoso-Otter-4821!#
> david    david.park-local@<your-tenant>.onmicrosoft.com      Contoso-Wolf-7193!#
> emma     emma.wilson-local@<your-tenant>.onmicrosoft.com     Contoso-Lynx-2056!#
> ...
> ```
>
> The file is gitignored (`/local-dev-credentials.txt` in `.gitignore`) and
> rewritten in full every time you re-run `setup-local`, but the **values**
> only change after a `-Cleanup` cycle.

The Entra side requires Application Developer + User Administrator (or higher) in the tenant where you ran `az login`. See the [Lab 0 prerequisites](lab-0.md#prerequisites) if you're missing those roles.

Override the region (default is `centralus`) when needed:

```powershell
$env:TF_VAR_location = "eastus2"
./infra/setup-local.ps1
```

## Step 2 — Validate Foundry connectivity

Run simple-agent from the repository root. Unlike the rest of the components in this repo, it defaults to the `Local` environment so plain `dotnet run` works with no flags or env vars — `setup-local` just wrote `appsettings.Local.json` next to the project, which `simple-agent` picks up automatically.

```bash
dotnet run --project src/simple-agent
```

Expected output (the joke varies — it's AI-generated):

```text
Using AI Foundry project endpoint: https://<your-foundry-endpoint>/
Model deployment:                  gpt-4.1
Auth mode:                         DefaultAzureCredential (Tenant: <your-tenant-id>)

Agent response:
 Why did the developer break up with the cloud?
 Because the relationship had too many issues... and none of them were resolved!
```

If you see an error, see the [Foundry troubleshooting section](../../local-development.md#foundry-quota-or-model-deployment-failure).

## How `simple-agent` works (your first Microsoft Agent Framework call)

If you're new to the Microsoft Agent Framework, [src/simple-agent/Program.cs](../../../src/simple-agent/Program.cs) is the smallest possible "hello world". The whole file is ~25 lines of code; here are the three calls that matter:

```csharp
// 1. DefaultAzureCredential — the only auth path in this repo. Walks
//    az CLI → Visual Studio → Managed Identity → Workload Identity in
//    order. Locally it picks up your `az login` token (which setup-local
//    granted "Cognitive Services OpenAI User" on the Foundry account).
var credential = new DefaultAzureCredential(
    new DefaultAzureCredentialOptions { TenantId = tenantId });

// 2. AIProjectClient is the Agent Framework's typed client over your
//    Foundry project. `AsAIAgent(...)` adapts it into a runnable agent —
//    pick a model deployment, set the system prompt, give it a name.
//    No tools, no memory, no orchestration: this agent does one thing.
AIAgent agent = new AIProjectClient(new Uri(endpoint), credential)
    .AsAIAgent(
        model: deploymentName,
        instructions: "You are a helpful and funny assistant who tells short jokes.",
        name: "Joker");

// 3. RunAsync sends the prompt + system instructions to the model and
//    returns when the model is done. For richer agents you'd pass a
//    `ChatMessage` history; for one-shot use, a string is enough.
var result = await agent.RunAsync("Tell me a joke about the cloud.");
```

The same three primitives — `DefaultAzureCredential` → `AIProjectClient.AsAIAgent(...)` → `agent.RunAsync(...)` — are how *every* agent in this repo is built. The richer agents in [Lab 2](lab-2.md) just add **tools** (MCP clients) and **multi-turn history** to the same `AsAIAgent` call.

## Step 3 — Run the full system

```bash
dotnet run --project src/AppHost
```

The Aspire AppHost starts all 8 components and a dashboard at **`https://localhost:15888`**. Open the Blazor UI at **`http://localhost:5008`** and you'll be redirected to `login.microsoftonline.com`.

**Sign in with one of the 8 test users from `local-dev-credentials.txt` at the repo root** (created by `setup-local` in Step 1 — see the callout above). For example, copy the `emma` row's UPN and password into the Microsoft sign-in dialog. The `-local` suffix on every UPN is intentional: it keeps Local-Track UPNs from colliding with the Full Azure Track if both run in the same tenant.

The BFF validates the JWT, looks up the signed-in UPN in `AzureAd:CustomerMap`, and scopes every downstream call to that customer's data.

For port maps, troubleshooting, and component-level details, see the [Local Development Guide](../../local-development.md).

## Verification checklist

- [ ] `terraform output` in `infra/terraform/local-dev/` returns a Foundry endpoint **and** a `bff_client_id`
- [ ] `simple-agent` returns a joke from AI Foundry
- [ ] All 8 services are green in the Aspire dashboard
- [ ] Blazor UI redirects to `login.microsoftonline.com` on first load
- [ ] After signing in as `emma.wilson-local@<your-tenant-domain>`, asking "what is my last order?" returns Emma's order data sourced from the in-memory CRM data

> **Cleanup** — leave the local environment running for the rest of the labs. Tear-down instructions live in the [Local Development Guide](../../local-development.md#cleanup).

## What's next

Lab 1 is complete. Continue with:

- **[Lab 2 — Single & Multi-Agent Workflows](lab-2.md)** — drive the existing CRM / Product / Orchestrator agents directly, then add a third specialist (Returns Agent) without touching the others.
- **[Lab 3 — Human-in-the-Loop Workflows](lab-3.md)** — build an ambient, durable refund-risk workflow with three parallel agents, an aggregator, and a Blazor operations dashboard for review.
