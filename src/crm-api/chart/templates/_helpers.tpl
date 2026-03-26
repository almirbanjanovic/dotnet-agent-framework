{{/*
=============================================================================
Standard Helm template helpers for Contoso Outdoors services
=============================================================================
*/}}

{{/*
Expand the name of the chart.
Truncated to 63 chars (Kubernetes name limit) with trailing dashes removed.
*/}}
{{- define "service.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Create a fully qualified app name.
Uses the release name if it differs from the chart name; otherwise just chart name.
Truncated to 63 chars for Kubernetes compatibility.
*/}}
{{- define "service.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{/*
Create chart name and version for the chart label.
*/}}
{{- define "service.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Standard Kubernetes labels applied to all resources.
Follows the app.kubernetes.io label convention for consistency.
*/}}
{{- define "service.labels" -}}
helm.sh/chart: {{ include "service.chart" . }}
{{ include "service.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
app.kubernetes.io/part-of: contoso-outdoors
{{- end }}

{{/*
Selector labels — used by Deployments and Services to match pods.
These MUST NOT change after initial deployment (immutable selector).
Also used as NetworkPolicy selectors for pod-to-pod communication rules.
*/}}
{{- define "service.selectorLabels" -}}
app.kubernetes.io/name: {{ include "service.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{/*
Service account name.
If serviceAccount.create is true, uses the generated fullname.
If false, uses the explicitly provided name (Terraform-managed SA).
*/}}
{{- define "service.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "service.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
Container image reference with tag fallback to Chart.appVersion.
*/}}
{{- define "service.image" -}}
{{- $tag := default .Chart.AppVersion .Values.image.tag }}
{{- printf "%s:%s" .Values.image.repository $tag }}
{{- end }}

{{/*
Namespace helper — allows override via values, defaults to release namespace.
*/}}
{{- define "service.namespace" -}}
{{- default .Release.Namespace .Values.namespaceOverride }}
{{- end }}
