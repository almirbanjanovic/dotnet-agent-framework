# Application Gateway for Containers — Routing Manifests

These manifests wire up the **Application Gateway for Containers (AGC)** that
Terraform provisions in `infra/terraform/main.tf` (`module.agc`) to the in-cluster
Kubernetes Services that Helm installs. Together they let users on the public
internet reach the Blazor UI and the BFF API through the AGC frontend FQDN.

```
Internet
    │  HTTPS
    ▼
AGC Frontend (auto-assigned FQDN, TLS terminated by AGC)
    │  HTTP
    ▼
ALB Controller in AKS  ── reads ──►  Gateway + HTTPRoute (this folder)
    │
    ├──►  Service blazor-ui  (path /, /authentication/*)
    └──►  Service bff-api    (path /api/*)
```

## Prerequisites — install the ALB Controller (one-time per cluster)

Terraform provisions the **AGC resource itself** but does **not** install the
ALB Controller (the Kubernetes operator that watches `Gateway` and `HTTPRoute`
custom resources and programs the AGC). That install happens once via the
Microsoft-published Helm chart, and is a manual step in
[Lab 1 Step 4](../../../../docs/lab-1.md).

The short version (full instructions in
[the upstream BYO Helm guide](https://learn.microsoft.com/en-us/azure/application-gateway/for-containers/quickstart-deploy-application-gateway-for-containers-alb-controller-helm)):

```bash
# 1. Get cluster credentials
az aks get-credentials --resource-group "$RG" \
  --name "aks-${BASE_NAME}-${ENV}-${LOC}" --overwrite-existing

# 2. Create a managed identity for the ALB Controller, federate it to the
#    well-known SA `azure-alb-system/alb-controller-sa`, and grant it
#    `AppGw for Containers Configuration Manager` on the AGC plus
#    `Network Contributor` on the AGC subnet.
#    (See the upstream guide for the exact `az identity` / `az role assignment`
#    commands — they reference the AKS OIDC issuer URL terraform outputs.)

# 3. Helm-install the controller chart
helm install alb-controller \
  oci://mcr.microsoft.com/application-lb/charts/alb-controller \
  --version 1.7.9 \
  --namespace azure-alb-system --create-namespace \
  --set albController.podIdentity.clientID=<ALB_UAMI_CLIENT_ID>

# 4. Verify
kubectl get pods -n azure-alb-system           # alb-controller-* should be Running
kubectl get gatewayclass azure-alb-external    # should be `Accepted: True`
```

## Apply the routing manifests

After the ALB Controller is installed and the GatewayClass is `Accepted`:

```bash
# Pull the AGC frontend resource ID Terraform exported
AGC_FRONTEND_ID=$(terraform -chdir=infra/terraform output -raw agc_frontend_id)

# Render the templates with envsubst (Linux/macOS) or PowerShell substitution
export AGC_FRONTEND_ID
envsubst < infra/k8s/manifests/gateway/application-loadbalancer.yaml.template \
  | kubectl apply -f -

# Static manifests apply directly
kubectl apply -f infra/k8s/manifests/gateway/gateway.yaml
kubectl apply -f infra/k8s/manifests/gateway/httproute-blazor-ui.yaml
kubectl apply -f infra/k8s/manifests/gateway/httproute-bff-api.yaml

# Verify
kubectl get gateway -n contoso          # PROGRAMMED=True after ~30s
kubectl get httproute -n contoso        # ACCEPTED=True
```

The Blazor UI is then reachable at `https://<AGC_FRONTEND_FQDN>/` (run
`terraform -chdir=infra/terraform output -raw agc_frontend_fqdn`).

## Why these are not auto-applied by Terraform

The Gateway API CRDs (`Gateway`, `HTTPRoute`, `ApplicationLoadBalancer`) are
installed **by the ALB Controller chart** — they don't exist in the cluster
until that chart runs. Trying to `kubectl_manifest` them from Terraform before
the chart is installed breaks `terraform plan`. They are therefore applied
manually after the Helm install completes.
