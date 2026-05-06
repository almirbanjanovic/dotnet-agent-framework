# Labs

The labs are split by **track** so each folder is a self-contained read-through. Pick the track that matches what you're trying to do and stay in that folder for the rest of the labs.

| | **[Local Track](local/)** *(Foundry only)* | **[Full Azure Track](full-azure/)** *(production-shaped)* |
|---|---|---|
| Best for | Inner loop, demos, agent prompt iteration | End-to-end testing, security/identity work, production patterns |
| Setup time | ~10 min | ~45–60 min |
| Cost | ~$1–5/day (Foundry tokens only) | ~$50–100/day |
| Azure resources | 1 (Foundry account + 2 model deployments) | 14+ (Foundry, Cosmos×2, AI Search, AKS, ACR, Storage, Key Vault, identities, networking) |
| Where the 8 services run | `dotnet run` (Aspire) on your laptop | AKS pods (Helm + workload identity) |
| Data | In-memory from `data/` | Cosmos DB + AI Search + Blob Storage |
| User auth | Microsoft Entra ID via MSAL (8 test users in your tenant) | Microsoft Entra ID via MSAL (8 test users in your tenant) |
| Bootstrap script | `./infra/setup-local.ps1` | `./infra/init.ps1` then `./infra/deploy.ps1` |

## Lab map

| # | Local Track | Full Azure Track | Topic |
|---|---|---|---|
| 0 | [Local — Lab 0](local/lab-0.md) | [Full Azure — Lab 0](full-azure/lab-0.md) | Bootstrap (tools, accounts, one-time setup) |
| 1 | [Local — Lab 1](local/lab-1.md) | [Full Azure — Lab 1](full-azure/lab-1.md) | Infrastructure, validation, data seeding |
| 2 | [Local — Lab 2](local/lab-2.md) | [Full Azure — Lab 2](full-azure/lab-2.md) | Single & multi-agent workflows (Microsoft Agent Framework) |
| 3 | [Local — Lab 3](local/lab-3.md) | [Full Azure — Lab 3](full-azure/lab-3.md) | Human-in-the-loop, durable, ambient agent workflows |

You **can** switch tracks later, but the two tracks share no Azure state — anything you provisioned for the other track stays where it is.
