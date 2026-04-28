# Terraform Test Implementation Notes

## Architecture

```
infra-tests/
└── terraform/
    ├── run-tests.ps1          ← PowerShell entry point (Windows)
    ├── run-tests.sh           ← Bash entry point (Unix/CI)
    ├── local-dev/             ← Tests for local-dev module
    └── README.md              ← User documentation
```

## Test Execution Flow

### PowerShell (run-tests.ps1)
1. Resolves script directory (works with both direct execution and sourcing)
2. Changes to `infra/terraform/local-dev`
3. Runs 4 sequential validation checks
4. Outputs colored results (PASS/FAIL)
5. Returns appropriate exit code

### Bash (run-tests.sh)
1. Uses `cd "$(dirname "$0")..."` to resolve relative paths
2. Changes to `infra/terraform/local-dev`
3. Runs 4 sequential validation checks using shell utilities (grep)
4. Returns appropriate exit code

## Why This Approach?

**Chosen approach:** Shell-based syntax + semantic validation  
**Not chosen:** Native `terraform test` framework

### Reasons:

| Factor | terraform test | Shell validation |
|--------|---|---|
| **Credentials required** | ✅ Yes | ❌ No |
| **Network access** | ✅ Yes | ❌ No |
| **Resource creation** | ✅ Yes | ❌ No |
| **CI/CD friendly** | ❌ Complex | ✅ Simple |
| **Runs without config** | ❌ No | ✅ Yes |
| **Execution time** | ~30-60s | ~1s |
| **Deterministic** | ❌ (cloud dependent) | ✅ Yes |

## Test Cases

### Test 1: terraform init
```powershell
terraform init -backend=false -input=false
```
- **-backend=false** — Don't configure backend (local mode)
- **-input=false** — No interactive prompts
- **Validates:** Provider configuration, module source URLs, HCL structure

### Test 2: terraform validate
```powershell
terraform validate
```
- **Validates:** 
  - HCL syntax correctness
  - Reference validity (all referenced variables/resources exist)
  - Type consistency
  - Argument requirements

### Test 3: Outputs Check
```bash
grep -q "output \"foundry_endpoint\"" outputs.tf
```
- **Validates:** All 4 required outputs are declared
- **Why:** Downstream code expects these outputs
- **Outputs:**
  - `foundry_endpoint` — Used by .NET app to connect to Azure OpenAI
  - `foundry_api_key` — Auth credential
  - `chat_deployment_name` — Model selection
  - `embedding_deployment_name` — Search capability

### Test 4: Variables Check
```bash
grep -A2 "variable \"location\"" variables.tf | grep -q "default"
```
- **Validates:** All variables have sensible defaults
- **Why:** Module should be usable without required inputs
- **Variables checked:** location, base_name, environment, chat_model_name, chat_model_version, embedding_model_name, embedding_model_version, resource_group_name

## Key Design Decisions

1. **No state files** — Tests use `-backend=false` to avoid state management
2. **No provider authentication** — `terraform validate` only checks syntax/semantics
3. **Fast execution** — Grep-based file validation is instant
4. **Cross-platform** — Both PowerShell and Bash versions for all environments
5. **No dependencies** — Only Terraform CLI and shell tools (no extra packages)
6. **Fail-fast design** — `set -e` / `$ErrorActionPreference = "Stop"` for immediate exit on failure

## Extension Points

To add more tests:

1. **Verify naming patterns:**
   ```bash
   grep -E 'name.*=.*"rg-' main.tf
   ```

2. **Validate resource properties:**
   ```bash
   grep -q 'local_auth_enabled.*=.*true' main.tf
   ```

3. **Check module parameters:**
   ```bash
   grep -q 'sku_name.*=.*"S0"' main.tf
   ```

4. **Count resources (when using terraform plan with mock data):**
   ```powershell
   terraform plan -json | jq '.resource_changes | length'
   ```

## Maintenance Notes

- Tests should run in ~1 second
- Tests should work without Azure credentials
- Both test scripts should stay in sync
- Update README.md when adding/modifying tests
- Test paths are relative to repository root

## Future Enhancements

- [ ] Add GitHub Actions workflow to run tests on every commit
- [ ] Add pre-commit hook to validate before pushing
- [ ] Add tfplan JSON parsing for resource count validation
- [ ] Add linting with `terraform fmt` check
- [ ] Add documentation validation for variables and outputs
