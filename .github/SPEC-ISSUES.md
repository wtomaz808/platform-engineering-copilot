# Spec Issues — Ready to File

These specs capture known remaining work. File each one as a GitHub Issue using the **Feature Spec** or **Enhancement** template.

---

## 1. Wire Drift Detection to Real Azure Data

**Template:** Feature Spec
**Labels:** `type/feature`, `area/admin-api`, `area/admin-ui`, `area/compliance`, `priority/high`

**Summary:**
The Drift Detection page currently shows demo/placeholder data. Replace with real Azure Resource Graph queries that compare actual resource configurations against baseline templates or policy assignments.

**Motivation:**
Platform teams need to detect when infrastructure drifts from approved configurations. Without real data, the drift dashboard provides no operational value.

**Affected Components:** Admin API, Admin Client (Blazor UI)

**Acceptance Criteria:**
- [ ] Admin API calls Azure Resource Graph to detect configuration drift
- [ ] Drift results include resource name, resource group, drift type, and delta
- [ ] Admin UI renders real drift data with severity indicators
- [ ] Empty state when no drift detected
- [ ] Loading spinner during fetch

**API Changes:**
- `GET /api/compliance/drift` — returns real drift scan results

**Technical Notes:**
- Follow the same pattern as `ComplianceController.cs` (real Azure REST API calls, 5-min cache)
- Use `GetAzureAccessToken()` for auth (consider extracting shared helper — see Issue #3)
- Azure Resource Graph query: compare `resources` against policy assignment baselines

---

## 2. Wire Health Status to Real Azure Data

**Template:** Feature Spec
**Labels:** `type/feature`, `area/admin-api`, `area/admin-ui`, `priority/high`

**Summary:**
The Health Status page currently shows demo data. Replace with real Azure Resource Health and Service Health API calls.

**Motivation:**
Operators need a live view of resource health to respond to outages and degraded services.

**Affected Components:** Admin API, Admin Client (Blazor UI)

**Acceptance Criteria:**
- [ ] Admin API queries Azure Resource Health API (`Microsoft.ResourceHealth/availabilityStatuses`)
- [ ] Results include resource name, health status, reason, and last updated time
- [ ] Admin UI displays health status with color-coded indicators (Available/Degraded/Unavailable)
- [ ] Service Health alerts displayed separately
- [ ] Loading spinner during fetch

**API Changes:**
- `GET /api/health/resources` — returns resource health statuses
- `GET /api/health/alerts` — returns service health alerts

**Technical Notes:**
- ARM endpoint: `{resourceId}/providers/Microsoft.ResourceHealth/availabilityStatuses?api-version=2023-07-01`
- Service Health: `subscriptions/{subId}/providers/Microsoft.ResourceHealth/events?api-version=2023-10-01-preview`
- Same auth pattern as existing Azure controllers

---

## 3. Extract Shared Azure Auth Helper

**Template:** Enhancement
**Labels:** `type/chore`, `area/admin-api`, `priority/medium`

**Summary:**
`GetAzureAccessToken()` is duplicated in `AzureResourcesController.cs` and `ComplianceController.cs` (and will be needed by Health, Drift, Cost controllers). Extract into a shared service.

**Motivation:**
Violates DRY principle. Adding new controllers that call Azure APIs requires copy-pasting auth logic.

**Affected Components:** Admin API

**Acceptance Criteria:**
- [ ] Shared `IAzureAuthService` with `GetAccessTokenAsync()` method
- [ ] Handles all 3 credential types (credentials, servicePrincipal, managedIdentity)
- [ ] Registered as singleton in DI
- [ ] `AzureResourcesController` and `ComplianceController` refactored to use it
- [ ] No behavioral changes to existing endpoints

**Technical Notes:**
- Place in `Services/AzureAuthService.cs` in the Admin.API project
- Also consider extracting the `HttpClient` + ARM base URL logic
- Constitution rule: "Extract shared helpers when 3+ controllers need the same pattern" — this qualifies

---

## 4. Environment Provisioning End-to-End

**Template:** Feature Spec
**Labels:** `type/feature`, `area/admin-api`, `area/admin-ui`, `area/infra`, `priority/high`

**Summary:**
Enable platform teams to provision new environments (dev/staging/prod resource groups with baseline resources) from the Admin UI. This is the core self-service capability.

**Motivation:**
Self-service environment provisioning is the primary value proposition of the platform engineering copilot.

**Affected Components:** Admin API, Admin Client (Blazor UI), Infrastructure (Bicep)

**Acceptance Criteria:**
- [ ] Admin UI form to select environment template, name, region, and subscription
- [ ] Admin API triggers ARM deployment using Bicep templates
- [ ] Deployment status tracked and displayed (Running/Succeeded/Failed)
- [ ] Environment appears in the Environments page after provisioning
- [ ] Provisioning logs available for troubleshooting

**API Changes:**
- `POST /api/environments/provision` — triggers new environment deployment
- `GET /api/environments/{id}/status` — returns deployment status

**UI Changes:**
- "New Environment" button on Environments page
- Provisioning wizard/form with template selection
- Deployment progress indicator

**Technical Notes:**
- Use ARM deployment API: `PUT subscriptions/{subId}/resourcegroups/{rg}/providers/Microsoft.Resources/deployments/{name}`
- Bicep templates in `infra/bicep/` — may need modular templates per environment type
- Consider async deployment with polling (ARM deployments can take minutes)
- Constitution: Bicep is the primary IaC tool

---

## 5. ADO Server PAT Scope Documentation

**Template:** Enhancement
**Labels:** `type/enhancement`, `area/devportal`, `documentation`, `priority/low`

**Summary:**
Document the exact ADO PAT scopes required for full Developer Portal functionality (repos, work items, pipelines, projects).

**Motivation:**
Users setting up ADO Server integration need to know which PAT scopes to enable. Currently undocumented.

**Affected Components:** Documentation

**Acceptance Criteria:**
- [ ] `docs/AUTHENTICATION.md` updated with ADO PAT scope requirements
- [ ] Table mapping features to required scopes
- [ ] Troubleshooting section for common auth errors (401/403)

---

## 6. Cost Management Dashboard — Real Data

**Template:** Feature Spec
**Labels:** `type/feature`, `area/admin-api`, `area/admin-ui`, `priority/medium`

**Summary:**
Wire the Cost Management page to real Azure Cost Management APIs for actual spend data, forecasts, and optimization recommendations.

**Motivation:**
Cost visibility is a core platform engineering capability. Demo data provides no operational value.

**Affected Components:** Admin API, Admin Client (Blazor UI)

**Acceptance Criteria:**
- [ ] Admin API queries Azure Cost Management API for actual spend
- [ ] Results include cost by resource group, service, and time period
- [ ] Admin UI renders cost charts and tables with real data
- [ ] Cost trend over last 30 days
- [ ] Budget alerts if configured

**API Changes:**
- `GET /api/cost/summary` — returns cost summary for subscription
- `GET /api/cost/by-resource-group` — returns cost breakdown by resource group

**Technical Notes:**
- Azure Cost Management API: `subscriptions/{subId}/providers/Microsoft.CostManagement/query`
- Requires `Cost Management Reader` role on subscription
- Consider caching (cost data doesn't change minute-to-minute — 30-min cache reasonable)
