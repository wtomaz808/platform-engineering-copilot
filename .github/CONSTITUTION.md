# Platform Engineering Copilot — Constitution

> This document defines the non-negotiable principles, technology choices, and conventions for this project.
> Every spec, PR, and implementation must conform to these rules. If a change contradicts the constitution,
> the constitution must be amended first via a PR with team review.

**Effective:** March 2026
**Maintainer:** @azurenoops

---

## 1. Mission

Deliver an AI-powered platform engineering copilot that enables self-service infrastructure provisioning,
compliance scanning, cost management, and environment governance — targeting Azure Government with
NIST 800-53 / FedRAMP High compliance.

---

## 2. Architecture Principles

### 2.1 Container Topology

The system runs as **5 Docker containers** orchestrated via Docker Compose. No containers may be merged or split without a constitution amendment.

| Container | Port | Purpose |
|-----------|------|---------|
| `pec-sqlserver` | 1433 | SQL Server — persistent data store |
| `pec-mcp` | 5100 | MCP Server — AI agent orchestration (HTTP + stdio) |
| `pec-chat` | 5001 | Chat UI — end-user conversational interface |
| `pec-admin-api` | 5050 | Admin API — REST API for management operations |
| `pec-admin-client` | 5003 | Admin Client — Blazor WASM web UI for platform admins |

### 2.2 Communication Rules

- **Admin Client → Admin API only.** The Blazor client never calls MCP, Azure, ADO, or GitHub directly.
- **Admin API is the gateway** to all external services (Azure ARM, ADO, GitHub, MCP).
- **Chat → MCP Server.** The Chat UI communicates exclusively with the MCP Server.
- **MCP Server is independent.** It serves both HTTP clients and AI clients (stdio). The Admin API does NOT proxy through MCP for Azure data — it calls Azure REST APIs directly.

### 2.3 Agent Architecture

- All AI agents extend `BaseAgent`; all tools extend `BaseTool`.
- 7 specialized agents with 52+ tools, coordinated via `PlatformAgentGroupChat`.
- Agents live in `Platform.Engineering.Copilot.Agents`.
- New agents must follow the BaseAgent/BaseTool pattern — no one-off implementations.

---

## 3. Technology Stack

These are the locked technology choices. Switching any of these requires a constitution amendment.

### 3.1 Runtime & Frameworks

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 9.0 | Runtime for all server projects |
| ASP.NET Core | 9.0 | Admin API, MCP Server, Chat Server |
| Blazor WebAssembly | 9.0 | Admin Client (client-side SPA) |
| Entity Framework Core | 9.0 | ORM for SQL Server |
| SQL Server | 2022 | Persistent data store |

### 3.2 Infrastructure & DevOps

| Technology | Purpose |
|------------|---------|
| Docker & Docker Compose | Local development, testing, deployment orchestration |
| Bicep | Azure infrastructure-as-code (primary IaC) |
| Terraform | Alternative IaC (in `infra/terraform/`) |
| Azure CLI (`az`) | Azure resource management, authentication, scripting |
| Kubernetes (AKS) | Production deployment target (manifests in `infra/kubernetes/`) |

### 3.3 Azure Services

| Service | Usage |
|---------|-------|
| Azure Resource Manager (ARM) | Resource provisioning & management |
| Azure Policy | Compliance policy evaluation |
| Microsoft Defender for Cloud | Security assessments |
| Azure Cost Management | Cost analysis & optimization |
| Azure Resource Graph | Cross-subscription resource queries |
| Azure Key Vault | Secrets management |
| Azure Monitor | Logging & diagnostics |

### 3.4 External Integrations

| Integration | Library / Method |
|-------------|-----------------|
| GitHub | Octokit 13.0.1 (repos, actions) |
| Azure DevOps (Services & Server) | REST API with PAT Basic auth |
| Azure Identity | `Azure.Identity` SDK (ClientSecretCredential, UsernamePasswordCredential, ManagedIdentityCredential) |

### 3.5 Client-Side Libraries

| Library | Purpose |
|---------|---------|
| Bootstrap 5 | CSS framework |
| Font Awesome | Icons |
| Blazored.Toast | Toast notifications |
| Blazored.Modal | Modal dialogs |
| Blazored.LocalStorage | Client-side settings persistence |

---

## 4. Cloud Target

- **Primary:** Azure Government (`AzureUSGovernment`)
- **Secondary:** Azure Commercial (`AzureCloud`)
- ARM endpoints are determined dynamically by the `CloudEnvironment` integration setting.
- **No AWS or GCP dependencies** — ever.

---

## 5. Authentication

### 5.1 Azure Authentication

Three modes, configurable via Settings > Integrations:

| Mode | Credential | Use Case |
|------|------------|----------|
| `credentials` | `UsernamePasswordCredential` | Development / demo |
| `servicePrincipal` | `ClientSecretCredential` | Automated / CI |
| `managedIdentity` | `ManagedIdentityCredential` | Production (ACI/AKS) |

All Azure auth goes through `Azure.Identity`. No hardcoded tokens or connection strings.

### 5.2 ADO Authentication

- PAT (Personal Access Token) with Basic auth header.
- Supports both ADO Services (cloud) and ADO Server (on-prem).
- For ADO Server: URL structure is `{serverUrl}/{collection}/{project}/_apis/...`.
- The PAT must have scopes: Code (Read), Work Items (Read), Build (Read), Project (Read).

### 5.3 GitHub Authentication

- PAT via Octokit client.
- Stored in integration settings file.

### 5.4 Credential Storage

- All credentials stored in `/app/settings/integrations-settings.json`.
- Docker volume `admin-api-settings` persists this file across container restarts.
- Never log credentials. Never return credentials in API responses.

---

## 6. Coding Conventions

### 6.1 Project Structure

```
src/
  Platform.Engineering.Copilot.Admin.API/       # REST API controllers, DTOs
  Platform.Engineering.Copilot.Admin.Client/     # Blazor WASM pages, services, models
  Platform.Engineering.Copilot.Agents/           # BaseAgent/BaseTool implementations
  Platform.Engineering.Copilot.Chat/             # Chat server
  Platform.Engineering.Copilot.Core/             # Shared types, utilities
  Platform.Engineering.Copilot.Mcp/              # MCP server
  Platform.Engineering.Copilot.State/            # EF Core DbContext, migrations
```

### 6.2 Backend (Admin API)

- **Controllers** handle HTTP routing and orchestration. Avoid duplicating Azure API call logic across controllers — extract shared helpers when 3+ controllers need the same pattern.
- **DTOs** live in `DTOs/` folder, one file per domain (e.g., `ComplianceDtos.cs`).
- **Integration settings** are loaded via `IntegrationsController.LoadSettings()` — this is the single source of truth for credentials.
- **No demo/mock data in controllers.** Return real API results or empty collections. Mark unimplemented features with `// TODO:` comments and return empty results, not fake data.

### 6.3 Frontend (Admin Client)

- **Pages** in `Pages/` — one `.razor` file per route.
- **Services** in `Services/` — thin HTTP wrappers over Admin API endpoints. No business logic.
- **Models** in `Models/ApiModels.cs` — client-side DTOs matching API responses.
- **Shared components** in `Shared/` (e.g., `MainLayout.razor`).

### 6.4 Naming

- Controllers: `{Domain}Controller.cs` (e.g., `ComplianceController.cs`)
- Client services: `{Domain}ApiService.cs` (e.g., `ComplianceApiService.cs`)
- DTOs: `{Name}Dto` suffix (e.g., `ComplianceSummaryDto`)
- Client models: No suffix (e.g., `ComplianceSummary`)

---

## 7. UI Conventions

- **Bootstrap 5** for all layout and styling. No custom CSS frameworks.
- **Font Awesome** for icons (`fa fa-*` classes).
- **Card-based layout** for data sections.
- **Pagination** for any list exceeding 10 items — Previous/Next buttons with item count display.
- **Loading spinners** during all async operations.
- **Toast notifications** (Blazored.Toast) for success, warning, and error feedback.
- **Consistent sidebar** navigation defined in `MainLayout.razor`.
- **Responsive design** — all pages must work at desktop and tablet widths.

---

## 8. Repository & Fork Rules

- **This project is a fork.** The working repository is `wtomaz808/platform-engineering-copilot`.
- **NEVER push, write, create labels, create issues, or make any changes to the upstream repo `azurenoops/platform-engineering-copilot`.** All work happens exclusively in the `wtomaz808` fork.
- Scripts, CI/CD, and automation must default to `Owner = "wtomaz808"`. Any reference to the upstream owner in tooling must be explicitly overridden and is considered a bug.
- Pull requests, issues, labels, and GitHub Actions run against `wtomaz808/platform-engineering-copilot` only.

---

## 9. Deployment Rules

- All Docker containers **must** have health checks.
- Docker volumes for persistent data (settings, SQL data).
- **No `--no-verify`** on git pushes.
- **No `--force`** pushes to `main`.
- Feature branches per issue (e.g., `feature/123-add-drift-scanning`).
- PRs required to merge into `main`.
- Containers must run as non-root user (`appuser`).

---

## 10. Testing

- Unit tests in `tests/Platform.Engineering.Copilot.Tests.Unit/`.
- Integration tests in `tests/Platform.Engineering.Copilot.Tests.Integration/`.
- Manual test cases documented in `docs/test cases/`.
- New features should include test coverage where feasible.

---

## 11. Security

- OWASP Top 10 awareness in all code reviews.
- No credentials in source code, environment variables preferred over config files for production.
- Input validation at API boundaries.
- CORS restricted to known origins.
- Container images scanned for vulnerabilities before deployment.

---

## 12. What This Constitution Is NOT

- **Not a roadmap** — that's GitHub Milestones and Issues.
- **Not documentation** — that's the `/docs` folder.
- **Not configuration** — that's `appsettings.json` and Docker Compose files.
- **Not a style guide** — follow standard .NET/C# conventions and the patterns already in the codebase.

---

## Amendments

To change any rule in this constitution:

1. Create a GitHub Issue with label `constitution-amendment`.
2. Describe the current rule, the proposed change, and the rationale.
3. Submit a PR modifying this file.
4. Requires team review and approval before merge.
