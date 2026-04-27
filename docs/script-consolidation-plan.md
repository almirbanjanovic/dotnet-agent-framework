# Script Consolidation Plan: `init.ps1` + `deploy.ps1` → Single Script

> **Author:** Stewie (Lead/Architect)
> **Date:** 2025-07-26
> **Status:** Proposal
> **Requested by:** Almir Banjanovic

---

## 1. Current State Analysis

### 1.1 `infra/init.ps1` — One-Time Bootstrap (~854 lines)

| Phase | What It Does | Creates/Modifies | Idempotent? |
|-------|-------------|-------------------|-------------|
| **Prerequisites** | Checks/installs `az`, `terraform`, `dotnet` via winget. Disables WAM broker + interactive subscription picker. | Nothing persistent | ✅ Yes |
| **Phase 1 — Authenticate** | `az login --scope graph`. Interactive subscription picker. Deployment mode prompt (Full vs Local-only). If full: installs/checks `gh`, runs `gh auth login`, detects/creates GitHub repo. Environment picker (dev/staging/production/custom). Region picker (30 regions). Recalculates derived names. | Sets `$SubscriptionId`, `$TenantId`, `$GitHubRepo`, `$GitHubEnv`, `$Location` | ✅ Yes (re-running is harmless) |
| **Phase 2 — Entra App + OIDC + RBAC** | Creates Entra app registration (`github-actions-<repo>`). Creates service principal. Adds OIDC federated credential for GitHub Actions. *Only runs in Full mode.* | App registration, SP, federated credential | ⚠️ Guarded — checks existence before creating each resource |
| **Phase 3 — GitHub Env/Secrets/Vars** | Creates GitHub environment. Sets 3 repo secrets (CLIENT_ID, TENANT_ID, SUBSCRIPTION_ID). Sets ~35 environment variables (model configs, SKUs, etc). *Only runs in Full mode.* | GitHub environment, secrets, variables | ✅ Yes (set is upsert) |
| **Phase 4 — Azure Backend** | Creates resource group. Detects deployer IP. Creates storage account (default-action Deny). Enables selected network access. Adds deployer IP to storage firewall. Creates `tfstate` blob container (with retry). Grants Contributor role to app on RG (full mode only). Grants `Application.ReadWrite.All` + admin consent (full mode only). Determines deployer identity (JWT decode). Grants `Storage Blob Data Contributor` to deployer. Grants Key Vault roles (Secrets Officer, Certificates Officer) to deployer. | RG, storage account, blob container, RBAC assignments | ⚠️ Mostly guarded — each resource checks existence. Storage account creation is one-time but update path exists. |
| **Phase 5 — Generate Config** | Writes `backend.hcl` (5 lines). Writes `<env>.tfvars` (~30 Terraform variables). Writes `deployments/<env>-<region>.env` (4 key-value pairs consumed by deploy.ps1). | 3 files on disk | ✅ Yes (overwrites existing) |
| **Final** | Prints summary with next steps. | Nothing | N/A |

**Key observations:**
- Phase 2 + 3 are conditional on `$LocalOnly` — they only run for Full (GitHub Actions) mode.
- Phase 4 is always needed — it creates the Terraform state backend.
- The `.env` file in `deployments/` is the handoff artifact between init and deploy.
- Heavy use of interactive prompts (`Read-Host`) throughout — subscription, mode, environment, region, confirmation.
- No `--SkipX` flags — all-or-nothing execution.

### 1.2 `infra/deploy.ps1` — Infrastructure Deployment (~1097 lines)

| Phase | What It Does | Dependencies | Idempotent? |
|-------|-------------|-------------|-------------|
| **Load Config** | Reads `.env` file from `deployments/`. If multiple, prompts user to pick. Parses `ENVIRONMENT`, `LOCATION`, `BASE_NAME`, `RESOURCE_GROUP`. | Requires `deployments/*.env` (created by init) | ✅ Yes |
| **Run Mode** | Interactive (pause between phases) or Auto (no pausing). | None | N/A |
| **Azure Login** | `az account clear` + `az login` (no Graph scope — avoids delegated token issue). Sets `AZURE_TENANT_ID`. Disables CAE env vars. | None | ✅ Yes |
| **Phase 0 — Agent Identity SP** | Finds or creates SP (`github-actions-<repo>` or `terraform-msgraph-<repo>`). Grants 6 Graph API permissions for Agent Identity Blueprints. Admin consent + propagation wait. Creates temporary 1-hour client secret. Sets `TF_VAR_msgraph_*` env vars. | Entra permissions | ⚠️ Guarded — finds existing SP, checks existing permissions. Secret is additive. |
| **Read Backend Config** | Reads `backend.hcl` for storage account name. Reads `<env>.tfvars`. | Requires init-generated files | ✅ Yes |
| **Display + Confirm** | Shows deployment config, gets confirmation. Gets deployer IP. | None | N/A |
| **Phase 1 — Open Firewalls** | Calls `Add-DeployerFirewallRules` — adds deployer IP to Key Vault, Storage, Cosmos DB, Foundry, AI Search firewalls. Waits 30s for propagation. | Resources must exist (except first run) | ✅ Yes (add is idempotent) |
| **Phase 2 — terraform init** | Ensures `Storage Blob Data Contributor` RBAC on backend storage. Runs `terraform init -upgrade -reconfigure -backend-config=backend.hcl`. Detects existing resources for import (Service Networking provider, 5 Entra test users). Sets `TF_VAR_import_*` and `TF_VAR_existing_user_ids`. | Backend storage must exist (init Phase 4) | ✅ Yes |
| **Phase 3 — terraform validate** | `terraform validate`. | Terraform initialized | ✅ Yes |
| **Soft-delete purge** | Purges soft-deleted Cognitive Services accounts and Key Vaults. | None | ✅ Yes |
| **Phase 4 — terraform plan** | `terraform plan -var-file=<env>.tfvars -out=tfplan`. | Valid config | ✅ Yes |
| **Phase 5 — terraform apply** | `terraform apply tfplan`. On failure: runs Azure Policy diagnostic (lists deny policies). Cleans up tfplan file. | Plan file | ⚠️ Terraform is idempotent, but failures leave partial state |
| **Phase 6 — Seed CRM Data** | Reads Cosmos endpoint + DB name from Key Vault. Verifies RBAC with retry loop (12 attempts × 5s). Runs `dotnet run` in `src/seed-data/`. | Cosmos DB deployed, Key Vault accessible, deployer IP firewalled | ✅ Yes (seed uses upsert) |
| **Phase 7 — Link Entra Users** | Reads 5 Entra object IDs from Key Vault secrets. Runs `dotnet run` with `ENTRA_MAPPING` env var. | Seed data + Entra users exist | ✅ Yes (upsert pattern) |
| **Cleanup (finally)** | Removes deployer IP from all firewalls. Deletes temporary SP client secret. Always runs (try/finally). | None | ✅ Yes |

**Key observations:**
- `deploy.ps1` depends on init's artifacts: `deployments/*.env`, `backend.hcl`, `<env>.tfvars`.
- Phase 0 (Agent Identity SP) partially duplicates init Phase 2 — both create/find the same app registration.
- Firewall open/close is bracketed with try/finally — good safety pattern.
- The Azure Policy diagnostic on apply failure is valuable and must be preserved.
- Phase 1's firewall logic handles "no resources yet" gracefully (first run: 0 resources to firewall).

---

## 2. Why Combine?

### 2.1 Pain Points of the Split

| Pain Point | Impact |
|-----------|--------|
| **Two logins** | User must `az login` twice — once in init (with Graph scope), once in deploy (without Graph scope). Different token requirements. |
| **Handoff via files** | init writes `deployments/<env>.env` + `backend.hcl` + `<env>.tfvars`. deploy reads them. If any file is missing/corrupt, deploy fails with "Run init first". |
| **Duplicated prerequisites** | Both scripts check `az`, `terraform`, `dotnet`. Deploy also checks `git`. |
| **Duplicated SP logic** | init creates `github-actions-<repo>` SP. deploy searches for it by name (with fallback to `terraform-msgraph-<repo>`). Same app registration, different code paths. |
| **User confusion** | "Do I run init again?" is a common question. The answer depends on whether backend storage exists, whether GitHub secrets changed, whether the region changed. |
| **Two sets of prompts** | init: subscription, mode, environment, region, confirmation. deploy: deployment selection, run mode, confirmation. Total: 7+ interactive prompts for a first-time deploy. |
| **Implicit ordering** | init must run before deploy. But there's no guard in deploy to detect "init was partially run" (e.g., RG exists but storage doesn't). |

### 2.2 Risks of Combining

| Risk | Mitigation |
|------|-----------|
| **Accidentally re-running bootstrap** | Auto-detect via storage account existence. If backend storage exists, skip bootstrap. |
| **Longer script** | Phase structure with skip flags. Individual phases are still isolated. |
| **Breaking existing users who already ran init** | Combined script reads existing `deployments/*.env`, `backend.hcl`, `<env>.tfvars` — if they exist, bootstrap is skipped. Zero manual migration. |
| **Different `az login` scopes** | init needs Graph scope for Entra app creation. deploy must NOT use Graph scope (causes Agent ID API rejection). Solution: login without Graph scope always; only add Graph scope if bootstrap phase needs it (re-login within bootstrap). |
| **GitHub CLI dependency** | Only required in Full mode. Make it a soft dependency — if `gh` isn't available, print values for manual entry. |

---

## 3. Proposed Combined Script Design

### 3.1 Recommended Name

**`infra/provision.ps1`** — "provision" encompasses both bootstrap and deployment. "deploy" implies ongoing deployments only.

Alternative: keep `infra/deploy.ps1` as the name (simpler migration, fewer doc changes). Recommendation: **keep `deploy.ps1`** to minimize churn.

### 3.2 Phase Structure

```
Phase 0: Prerequisites & Authentication
Phase 1: Bootstrap (was init.ps1 Phases 2-5) — idempotent, creates backend + Entra app + GitHub env + config files
Phase 2: Agent Identity SP (was deploy Phase 0) — creates/finds SP, grants Graph permissions, creates temp secret
Phase 3: Open Firewalls (was deploy Phase 1) — adds deployer IP to all resource firewalls
Phase 4: Terraform Init (was deploy Phase 2) — backend config, resource import detection
Phase 5: Terraform Validate (was deploy Phase 3) — syntax check + soft-delete purge
Phase 6: Terraform Plan (was deploy Phase 4) — preview changes
Phase 7: Terraform Apply (was deploy Phase 5) — provision all resources
Phase 8: Seed Data (was deploy Phase 6) — CSV → Cosmos DB
Phase 9: Link Entra Users (was deploy Phase 7) — Entra OID → Customer mapping
Cleanup: Close Firewalls + Delete Temp Secret (always runs)
```

### 3.3 Parameters / Skip Flags

```powershell
param(
    [switch]$SkipBootstrap,     # Skip Phase 1 (init) even if auto-detection would run it
    [switch]$SkipSeed,          # Skip Phase 8 (data seeding)
    [switch]$SkipEntraLinking,  # Skip Phase 9 (Entra user → customer linking)
    [switch]$Auto               # Non-interactive mode (no pausing between phases)
)
```

### 3.4 Auto-Detection Logic (Phase 0 → Phase 1 Decision)

```powershell
# ── Phase 0: Prerequisites & Authentication ──

# Check/install: az, terraform, dotnet
# az login (WITHOUT Graph scope — deploy-compatible)
# Subscription picker
# Load existing deployment config if available

$NeedBootstrap = $false

# Strategy: If deployments/*.env exists AND backend storage exists → skip bootstrap
if (Test-Path "$ScriptDir\deployments\*.env") {
    # Load the .env file (same logic as current deploy.ps1)
    $deployConfig = Parse-EnvFile $DeployEnvFile
    $ResourceGroup  = $deployConfig["RESOURCE_GROUP"]
    $StorageAccount = Derive-StorageAccountName $ResourceGroup

    # Probe: does the storage account exist?
    $null = az storage account show --resource-group $ResourceGroup --name $StorageAccount 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Done "Bootstrap already complete (found $StorageAccount)"
        $NeedBootstrap = $false
    } else {
        Write-Info "Deployment config exists but storage account missing — re-running bootstrap"
        $NeedBootstrap = $true
    }
} else {
    Write-Info "No deployment config found — running bootstrap"
    $NeedBootstrap = $true
}

if ($SkipBootstrap) {
    $NeedBootstrap = $false
    Write-Skip "Bootstrap skipped (--SkipBootstrap)"
}

# ── Phase 1: Bootstrap (conditional) ──
if ($NeedBootstrap) {
    # Re-login with Graph scope for Entra operations
    az login --scope https://graph.microsoft.com/.default | Out-Null

    # Deployment mode prompt (Full vs Local-only)
    # Environment picker
    # Region picker
    # Create Entra app + OIDC (Full mode)
    # Create GitHub env/secrets/vars (Full mode)
    # Create RG, storage account, blob container
    # RBAC assignments
    # Generate backend.hcl, <env>.tfvars, deployments/<env>.env

    # After bootstrap, clear stale Graph token and re-login cleanly
    az account clear 2>$null
    az login | Out-Null
}

# ── From here forward: identical to current deploy.ps1 flow ──
```

### 3.5 Detailed Phase Mapping

#### Phase 0: Prerequisites & Authentication (NEW — merged)

**Source:** init.ps1 Prerequisites + init.ps1 Phase 1 (auth only) + deploy.ps1 login block

```
FROM init.ps1:
  - Prerequisite checks (az, terraform, dotnet)
  - az config set (WAM broker, subscription picker)
FROM deploy.ps1:
  - az account clear + az login (no Graph scope)
  - Tenant ID extraction
  - CAE disable env vars (ARM_DISABLE_CAE, AZURE_DISABLE_CAE, HAMILTON_DISABLE_CAE)
NEW:
  - Auto-detection logic (does deployments/*.env + storage account exist?)
  - Load existing config or prompt for new
```

#### Phase 1: Bootstrap (CONDITIONAL — was init.ps1 Phases 1-5)

**Trigger:** `$NeedBootstrap -eq $true`

**Sub-steps (in order):**

1. **Re-login with Graph scope** — `az login --scope https://graph.microsoft.com/.default`
   - Required for Entra app operations. Only happens once per bootstrap.
2. **Subscription picker** — from init.ps1 Phase 1 (reuse exact code)
3. **Deployment mode** — Full (Azure + GitHub) vs Local-only
4. **GitHub auth** — `gh auth login` (Full mode only)
5. **GitHub repo detection/creation** — (Full mode only)
6. **Environment picker** — dev/staging/production/custom
7. **Region picker** — 30 regions grouped by geography
8. **Entra app + OIDC + RBAC** — from init.ps1 Phase 2 (Full mode only)
9. **GitHub env/secrets/vars** — from init.ps1 Phase 3 (Full mode only)
10. **Azure backend** — from init.ps1 Phase 4 (RG, storage, container, RBAC)
11. **Generate config files** — from init.ps1 Phase 5 (backend.hcl, .tfvars, .env)
12. **Re-login without Graph scope** — `az account clear && az login` (clean token for deploy phases)

#### Phase 2: Agent Identity SP (was deploy.ps1 Phase 0)

**Source:** deploy.ps1 lines 465-598 — verbatim.

No changes needed. Already idempotent (finds existing SP, checks permissions).

#### Phase 3: Open Firewalls (was deploy.ps1 Phase 1)

**Source:** deploy.ps1 `Add-DeployerFirewallRules` function + Phase 1 block.

No changes needed. Handles "no resources yet" (first run after bootstrap) gracefully — 0 Key Vaults, 0 Cosmos DBs, etc.

#### Phase 4: Terraform Init (was deploy.ps1 Phase 2)

**Source:** deploy.ps1 lines 659-775.

No changes needed. RBAC check on backend storage is already there. Import detection is already idempotent.

#### Phase 5: Terraform Validate + Soft-Delete Purge (was deploy.ps1 Phase 3 + purge block)

**Source:** deploy.ps1 lines 777-822.

No changes needed.

#### Phase 6: Terraform Plan (was deploy.ps1 Phase 4)

**Source:** deploy.ps1 lines 824-844.

No changes needed.

#### Phase 7: Terraform Apply (was deploy.ps1 Phase 5)

**Source:** deploy.ps1 lines 846-941.

No changes needed. Azure Policy diagnostic on failure is preserved.

#### Phase 8: Seed Data (CONDITIONAL — was deploy.ps1 Phase 6)

**Source:** deploy.ps1 lines 943-994.

**Change:** Wrap in `if (-not $SkipSeed)` guard.

```powershell
if (-not $SkipSeed) {
    # ... existing seed logic ...
} else {
    Write-Skip "Seed data skipped (--SkipSeed)"
}
```

#### Phase 9: Link Entra Users (CONDITIONAL — was deploy.ps1 Phase 7)

**Source:** deploy.ps1 lines 996-1039.

**Change:** Wrap in `if (-not $SkipEntraLinking)` guard.

```powershell
if (-not $SkipEntraLinking) {
    # ... existing linking logic ...
} else {
    Write-Skip "Entra linking skipped (--SkipEntraLinking)"
}
```

#### Cleanup (was deploy.ps1 finally block)

**Source:** deploy.ps1 lines 1076-1096.

No changes needed. Already in try/finally.

### 3.6 GitHub Environment Secrets — Graceful Handling

```powershell
# In Phase 1 (Bootstrap), GitHub sub-steps:
$GhAvailable = (Get-Command gh -ErrorAction SilentlyContinue) -ne $null
$GhAuthed = $false
if ($GhAvailable) {
    $ghStatus = gh auth status 2>&1
    $GhAuthed = $ghStatus -match "Logged in"
}

if ($GhAvailable -and $GhAuthed) {
    # Automated: set secrets + variables via gh CLI (existing code)
    gh secret set AZURE_CLIENT_ID --repo "$GitHubRepo" --body "$AppClientId"
    # ... etc
} else {
    # Manual fallback: print values
    Write-Host ""
    Write-Host "    GitHub CLI not available or not authenticated." -ForegroundColor Yellow
    Write-Host "    Set these manually in GitHub → Settings → Secrets:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    Repository Secrets:" -ForegroundColor Cyan
    Write-Host "      AZURE_CLIENT_ID:       $AppClientId"
    Write-Host "      AZURE_TENANT_ID:       $TenantId"
    Write-Host "      AZURE_SUBSCRIPTION_ID: $SubscriptionId"
    Write-Host ""
    Write-Host "    Environment Variables ($GitHubEnv):" -ForegroundColor Cyan
    foreach ($kv in $envVars.GetEnumerator()) {
        Write-Host "      $($kv.Key) = $($kv.Value)"
    }
    Write-Host ""
}
```

---

## 4. Idempotency Analysis

### init.ps1 Steps

| Step | Classification | Notes |
|------|---------------|-------|
| Install prerequisites (winget) | ✅ Already idempotent | `Get-Command` check before install |
| `az login` | ✅ Already idempotent | Re-login is harmless |
| `gh auth login` | ✅ Already idempotent | Checks `gh auth status` first |
| Create Entra app registration | ⚠️ Needs guard | Currently guarded: `az ad app list --display-name` before create |
| Create service principal | ⚠️ Needs guard | Currently guarded: `az ad sp show` before create |
| Add OIDC federated credential | ⚠️ Needs guard | Currently guarded: `az ad app federated-credential list` before create |
| Create GitHub environment | ✅ Already idempotent | `PUT` is upsert |
| Set GitHub secrets | ✅ Already idempotent | `gh secret set` is upsert |
| Set GitHub variables | ✅ Already idempotent | `gh variable set` is upsert |
| Create resource group | ⚠️ Needs guard | Currently guarded: `az group show` before create |
| Create storage account | ⚠️ Needs guard | Currently guarded: `az storage account show` before create |
| Create blob container | ⚠️ Needs guard | Currently guarded: retry loop with `az storage container show` |
| Grant Contributor RBAC | ⚠️ Needs guard | Currently guarded: `az role assignment list` before create |
| Grant Graph API permissions | ⚠️ Needs guard | Currently guarded: `az ad app permission list` check |
| Admin consent | ✅ Already idempotent | Re-applying consent is harmless |
| Grant Storage Blob Data Contributor | ⚠️ Needs guard | Currently guarded |
| Grant Key Vault roles | ⚠️ Needs guard | Currently guarded |
| Write backend.hcl | ✅ Already idempotent | Overwrites file |
| Write .tfvars | ✅ Already idempotent | Overwrites file |
| Write .env | ✅ Already idempotent | Overwrites file |

### deploy.ps1 Steps

| Step | Classification | Notes |
|------|---------------|-------|
| Read .env config | ✅ Already idempotent | Pure read |
| `az login` | ✅ Already idempotent | Re-login is harmless |
| Find/create Agent SP | ⚠️ Needs guard | Currently guarded: searches by name before create |
| Grant Graph API permissions | ⚠️ Needs guard | Currently guarded: checks existing perms |
| Create temp client secret | ❌ Not idempotent | Adds a new secret each run. **Mitigated:** secrets expire in 1 hour, and cleanup deletes all `terraform-deploy-*` credentials. Acceptable. |
| Add deployer IP to firewalls | ✅ Already idempotent | Add is idempotent (duplicate rules are no-ops or merge) |
| RBAC on backend storage | ⚠️ Needs guard | Currently guarded |
| `terraform init` | ✅ Already idempotent | `-reconfigure` handles re-init |
| Import detection | ✅ Already idempotent | Checks state before import |
| `terraform validate` | ✅ Already idempotent | Pure validation |
| Purge soft-deleted resources | ✅ Already idempotent | Purge non-existent resources is a no-op |
| `terraform plan` | ✅ Already idempotent | Read-only operation |
| `terraform apply` | ✅ Already idempotent | Terraform handles convergence |
| Azure Policy diagnostic | ✅ Already idempotent | Read-only diagnostic |
| Seed data (`dotnet run`) | ✅ Already idempotent | Upsert pattern in CrmSeeder |
| Entra user linking | ✅ Already idempotent | Upsert pattern |
| Remove deployer IPs from firewalls | ✅ Already idempotent | Remove non-existent rules is a no-op |
| Delete temp client secrets | ✅ Already idempotent | Delete non-existent credentials is a no-op |

### Summary

- **✅ Already idempotent:** 22 steps
- **⚠️ Guarded (check-before-create):** 13 steps — all already have guards in the current code
- **❌ Not idempotent:** 1 step (temp secret creation) — acceptable, cleaned up automatically

**Conclusion:** Both scripts are already designed for re-run safety. The combined script inherits this property with no additional idempotency work.

---

## 5. Migration Plan

### 5.1 File Changes

| File | Action | Rationale |
|------|--------|-----------|
| `infra/deploy.ps1` | **Rewrite** — becomes the combined script | Keep the name to minimize doc/lab changes |
| `infra/init.ps1` | **Replace with stub** | Stub prints "init is now integrated into deploy.ps1" and exits. Kept for 1 release cycle, then deleted. |
| `infra/deploy.sh` | **Update** | Bash equivalent should mirror the combined logic (or at minimum add a note) |
| `infra/init.sh` | **Replace with stub** | Same treatment as init.ps1 |
| `infra/deployments/*.env` | **No change** | Combined script reads existing .env files. Users who already ran init don't need to re-run anything. |
| `infra/terraform/backend.hcl` | **No change** | Still generated/read the same way |
| `infra/terraform/<env>.tfvars` | **No change** | Still generated/read the same way |

### 5.2 Backwards Compatibility

**Scenario: User already ran `init.ps1`, hasn't run `deploy.ps1` yet.**
- Combined `deploy.ps1` detects existing `deployments/*.env` + storage account → skips bootstrap → proceeds to deploy. ✅ No action needed.

**Scenario: User already ran both `init.ps1` and `deploy.ps1`.**
- Combined `deploy.ps1` detects existing config + storage → skips bootstrap → runs deploy (Terraform converges to no-op). ✅ No action needed.

**Scenario: Fresh user, never ran either script.**
- Combined `deploy.ps1` detects no config → runs bootstrap → runs deploy. ✅ Single command, full setup.

**Scenario: User re-runs `init.ps1` after migration.**
- Stub prints a message directing them to `deploy.ps1`. ✅ No confusion.

### 5.3 Documentation Updates

| Document | Change |
|----------|--------|
| `docs/lab-0.md` | Update to reference single `deploy.ps1` instead of `init.ps1 → deploy.ps1` flow |
| `docs/lab-1.md` | Remove "Run init first" prerequisite. Update deploy instructions. |
| `infra/README.md` | Update script inventory. Explain combined script, skip flags, auto-detect behavior. |
| `README.md` | Update any references to init.ps1 in the quick-start section |
| `.squad/decisions.md` | Add decision about script consolidation |

### 5.4 Stub for `init.ps1`

```powershell
#!/usr/bin/env pwsh
Write-Host ""
Write-Host "  ╔═══════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
Write-Host "  ║  init.ps1 has been merged into deploy.ps1                 ║" -ForegroundColor Yellow
Write-Host "  ║                                                           ║" -ForegroundColor Yellow
Write-Host "  ║  Run ./deploy.ps1 instead — it auto-detects whether      ║" -ForegroundColor Yellow
Write-Host "  ║  bootstrap is needed and handles everything.              ║" -ForegroundColor Yellow
Write-Host "  ║                                                           ║" -ForegroundColor Yellow
Write-Host "  ║  Skip flags:                                              ║" -ForegroundColor Yellow
Write-Host "  ║    ./deploy.ps1 -SkipBootstrap    (skip init phase)       ║" -ForegroundColor Yellow
Write-Host "  ║    ./deploy.ps1 -SkipSeed         (skip data seeding)     ║" -ForegroundColor Yellow
Write-Host "  ║    ./deploy.ps1 -SkipEntraLinking (skip Entra linking)    ║" -ForegroundColor Yellow
Write-Host "  ╚═══════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
Write-Host ""
exit 0
```

---

## 6. Relationship to Local Dev

**`setup-local.ps1`** (planned, not yet implemented per the local dev spec in `docs/foundry-only-deployment.md`) is a completely separate concern:

| Aspect | `deploy.ps1` (Azure) | `setup-local.ps1` (Local Dev) |
|--------|---------------------|-------------------------------|
| **Target** | Azure cloud resources | Local emulators + Foundry-only |
| **Terraform root** | `infra/terraform/` (20+ modules, K8s providers) | `infra/terraform/local-dev/` (2-3 resources) |
| **Auth model** | `DefaultAzureCredential` → managed identity | API keys, connection strings, emulators |
| **Resources** | AKS, ACR, Cosmos DB, AI Search, Key Vault, Storage, VNet, etc. | Cosmos emulator, Azurite, Foundry API key |
| **Runtime** | AKS pods | `dotnet run` on localhost |
| **Firewall concerns** | Complex open/close bracketing | None (local) |

**Verdict:** `setup-local.ps1` stays completely separate. No shared code, no shared Terraform, no shared complexity. The consolidation of init + deploy has zero impact on local dev.

---

## 7. Estimated Impact

### 7.1 Lines of Code

| Metric | Current | Combined | Delta |
|--------|---------|----------|-------|
| `init.ps1` | ~854 lines | ~20 lines (stub) | -834 |
| `deploy.ps1` | ~1097 lines | ~1450 lines | +353 |
| **Total** | ~1951 lines | ~1470 lines | **-481 lines** (~25% reduction) |

The reduction comes from:
- Eliminating duplicated prerequisites check (~30 lines)
- Eliminating duplicated `az login` flow (~15 lines)
- Eliminating duplicated SP discovery logic (~20 lines)
- Eliminating duplicated helper functions (`Write-Banner`, `Write-Phase`, `Write-Step`, etc.) (~100 lines)
- Eliminating the handoff .env file parsing logic in deploy (~50 lines)
- Removing init's standalone summary/banner code (~40 lines)

New code added:
- Auto-detection logic (~25 lines)
- Skip flag parameter block + guards (~15 lines)
- GitHub CLI graceful fallback (~20 lines)
- Token re-login between bootstrap and deploy (~10 lines)

### 7.2 Risk Assessment

| Risk | Severity | Likelihood | Mitigation |
|------|---------|------------|------------|
| Bootstrap runs when it shouldn't | Medium | Low | Storage account probe is reliable. `--SkipBootstrap` as escape hatch. |
| Bootstrap skipped when needed | Medium | Low | Clear "no config found" message + explicit prompt. |
| Token scope conflict (Graph vs no-Graph) | High | Medium | Re-login without Graph scope after bootstrap completes. Well-documented in deploy.ps1 comments already. |
| Longer script harder to debug | Low | Medium | Phase structure preserved. Phase numbers in all output. |
| `init.sh` / `deploy.sh` out of sync | Medium | High | Must update both `.ps1` and `.sh` variants simultaneously. |

### 7.3 What Could Break During Migration

1. **Token scoping** — The biggest risk. init uses `az login --scope graph`. deploy explicitly avoids this. The combined script must re-login between bootstrap and deploy phases. Already addressed in the design (Section 3.4).

2. **GitHub CLI prompts** — If `gh auth login` browser flow hangs (known issue), it blocks the entire combined script. Mitigated by making GitHub operations a soft dependency with manual fallback.

3. **Storage account creation timing** — init waits 30s after storage account creation, then 30s for firewall propagation. Combined script must preserve these waits. No change needed — existing code handles this.

4. **Existing CI/CD workflows** — GitHub Actions workflows reference `init.ps1` or `deploy.ps1` directly. Check `.github/workflows/*.yml` for references and update if needed.

5. **Lab instructions** — Students following lab-0 and lab-1 step-by-step will need updated instructions. Old instructions won't cause harm (stub redirects), but the flow changes.

---

## Appendix: Edge Cases

### Edge Case 1: Partial Bootstrap (RG exists, storage doesn't)

The auto-detection probes the storage account, not the RG. If the RG exists but storage doesn't, bootstrap runs and creates storage. The `az group create` has an existence check — it skips if the RG already exists. ✅ Handled.

### Edge Case 2: Storage account exists but container doesn't

Bootstrap checks storage account existence. If storage exists, bootstrap is skipped. But the container might be missing. **Mitigation:** The `terraform init` phase will fail if the container doesn't exist. Add a secondary probe for the blob container in the auto-detection logic:

```powershell
$null = az storage container show --name "tfstate" --account-name $StorageAccount --auth-mode login 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Info "Storage account exists but tfstate container missing — re-running bootstrap"
    $NeedBootstrap = $true
}
```

### Edge Case 3: Different environment than what's in .env

User has `dev-eastus2.env` from a previous init but wants to deploy to `staging`. **Solution:** If multiple .env files exist, prompt for selection (current deploy.ps1 behavior). If the desired environment doesn't have an .env file, run bootstrap for the new environment.

### Edge Case 4: `backend.hcl` missing but `deployments/*.env` exists

This shouldn't happen (init writes both), but could if files were manually deleted. **Solution:** If .env exists but `backend.hcl` doesn't, treat as needing bootstrap.

```powershell
if (-not (Test-Path "$TerraformDir\backend.hcl")) {
    Write-Info "backend.hcl missing — re-running bootstrap"
    $NeedBootstrap = $true
}
```

### Edge Case 5: User wants to re-run bootstrap intentionally

Force it with the inverse of `--SkipBootstrap` — add a `--ForceBootstrap` flag:

```powershell
param(
    [switch]$ForceBootstrap,    # Force Phase 1 even if auto-detection says skip
    [switch]$SkipBootstrap,     # Skip Phase 1 even if auto-detection says run
    [switch]$SkipSeed,
    [switch]$SkipEntraLinking,
    [switch]$Auto
)

if ($ForceBootstrap -and $SkipBootstrap) {
    throw "Cannot use both -ForceBootstrap and -SkipBootstrap"
}
```
