#!/bin/bash
set -e

cd "$(dirname "$0")/../../infra/terraform/local-dev"

echo "================================"
echo "Terraform Tests: local-dev"
echo "================================"
echo ""

echo "Test 1: terraform init succeeds"
terraform init -backend=false -input=false > /dev/null 2>&1
echo "  PASS"

echo "Test 2: terraform validate succeeds"
terraform validate > /dev/null 2>&1
echo "  PASS"

echo "Test 3: All expected outputs defined"
for output in foundry_endpoint foundry_api_key chat_deployment_name embedding_deployment_name; do
  if ! grep -q "output \"$output\"" outputs.tf; then
    echo "  FAIL: missing output $output"
    exit 1
  fi
done
echo "  PASS"

echo "Test 4: Module variables have sensible defaults"
# Check that all variables have defaults except those that should not
for var in location base_name environment chat_model_name chat_model_version embedding_model_name embedding_model_version; do
  if ! grep -A2 "variable \"$var\"" variables.tf | grep -q "default"; then
    echo "  FAIL: variable $var missing default"
    exit 1
  fi
done
# resource_group_name should have default = null (which is sensible)
if ! grep -A2 "variable \"resource_group_name\"" variables.tf | grep -q "default"; then
  echo "  FAIL: variable resource_group_name missing default"
  exit 1
fi
echo "  PASS"

echo ""
echo "================================"
echo "All 4 tests passed!"
echo "================================"
