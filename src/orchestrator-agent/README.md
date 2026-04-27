# Orchestrator Agent
> Intent classification and routing agent that delegates to CRM Agent and Product Agent.

Implemented as a .NET 9 minimal API with LLM-based intent classification and HTTP routing to specialist agents.

## Configuration

Required config keys (populated by config-sync from Key Vault):

| Key | Description |
|-----|-------------|
| `Foundry:Endpoint` | AI Foundry service endpoint |
| `Foundry:DeploymentName` | Model deployment name |
| `CrmAgent:BaseUrl` | CRM Agent base URL |
| `ProductAgent:BaseUrl` | Product Agent base URL |
| `AzureAd:TenantId` | Entra tenant ID for DefaultAzureCredential |

Run config-sync to populate: `cd src/config-sync && dotnet run -- <key-vault-uri> [environment]`

## How to run locally

```bash
cd src/orchestrator-agent
dotnet run
```

### Endpoints
- POST `/api/v1/chat` — routes to CRM or Product agent
- GET `/health` — liveness
- GET `/ready` — readiness (CRM agent, Product agent, Foundry)

## Architecture role

Orchestrator Agent is the single entry point for all agent chat requests from the BFF API. It classifies user intent and routes to either CRM Agent or Product Agent, then returns the specialist's response. It is stateless — conversation history is managed by the BFF.
