# Kubernetes NetworkPolicy Manifests

Pod-level network segmentation for the `contoso` namespace. Implements least-privilege
ingress and egress rules based on the architecture traffic flow.

## Why

The AKS cluster has Azure Network Policy (NPM) enabled (`network_policy = "azure"` in
the AKS Terraform module), but without NetworkPolicy manifests all pods in the namespace
can communicate freely. These policies close that gap (security finding **F-01**).

## Architecture — Allowed Traffic Flow

```
Internet → AGC Ingress ──┬── blazor-ui (static WASM)
                         └── bff-api
                              └── orchestrator-agent
                                   ├── crm-agent
                                   │    └── crm-mcp
                                   │         └── crm-api ──→ Cosmos DB (PE)
                                   └── product-agent
                                        └── knowledge-mcp ──→ AI Search (PE)

All agents ──→ Azure OpenAI (PE)
All services ──→ Key Vault (PE)
```

## Policy Structure

| File | Pod Selector | Ingress From | Egress To |
|---|---|---|---|
| `default-deny.yaml` | `{}` (all pods) | ✗ all denied | ✗ all denied |
| `bff-api.yaml` | `app.kubernetes.io/name: bff-api` | AGC namespace | orchestrator-agent, PE subnet, DNS |
| `blazor-ui.yaml` | `app.kubernetes.io/name: blazor-ui` | AGC namespace | DNS only |
| `orchestrator-agent.yaml` | `app.kubernetes.io/name: orchestrator-agent` | bff-api | crm-agent, product-agent, PE subnet, DNS |
| `crm-agent.yaml` | `app.kubernetes.io/name: crm-agent` | orchestrator-agent | crm-mcp, PE subnet, DNS |
| `product-agent.yaml` | `app.kubernetes.io/name: product-agent` | orchestrator-agent | knowledge-mcp, PE subnet, DNS |
| `crm-mcp.yaml` | `app.kubernetes.io/name: crm-mcp` | crm-agent | crm-api, PE subnet, DNS |
| `knowledge-mcp.yaml` | `app.kubernetes.io/name: knowledge-mcp` | product-agent | PE subnet, DNS |
| `crm-api.yaml` | `app.kubernetes.io/name: crm-api` | crm-mcp | PE subnet, DNS |

## Design Decisions

### Pod Selectors
Policies use `app.kubernetes.io/name: {service-name}` labels as pod selectors. This is the
Kubernetes standard label convention and matches the labels automatically produced by Helm
chart templates (see `docs/templates/helm-base/templates/_helpers.tpl`, `service.selectorLabels`).
No custom labels need to be added to Helm values.

### DNS Egress
Every policy allows egress to kube-dns (`k8s-app: kube-dns` in `kube-system`) on port 53
TCP/UDP. Without this, pods cannot resolve any hostnames — including private endpoint FQDNs
and in-cluster service names.

### External Services via Private Endpoints
Egress to Azure PaaS services (Cosmos DB, AI Search, Azure OpenAI, Key Vault) is allowed
on port 443 to the private endpoint subnet (`10.0.3.0/24`). If the VNet CIDR is customized
via Terraform variables, update the `cidr` value in each policy accordingly.

### AGC Ingress
`bff-api` and `blazor-ui` accept ingress from the `azure-alb-system` namespace where the
ALB Controller runs. The namespace selector uses `kubernetes.io/metadata.name`, a built-in
label set by Kubernetes on all namespaces.

### Default Deny
The `default-deny.yaml` policy matches all pods (`podSelector: {}`) and declares both
Ingress and Egress policy types with no rules — blocking everything by default. Per-service
policies then whitelist only the required paths.

## Applying the Policies

```bash
# Apply all policies at once
kubectl apply -f infra/k8s/network-policies/

# Verify policies are active
kubectl get networkpolicy -n contoso

# Test connectivity (from within a pod)
kubectl exec -n contoso deploy/bff-api -- wget -qO- --timeout=2 http://orchestrator-agent:80/health
```

## Updating

When adding a new service to the namespace:
1. Create a new `{service}.yaml` following the same pattern
2. Update upstream services' egress rules to allow traffic to the new service
3. Re-apply: `kubectl apply -f infra/k8s/network-policies/`
