# Platform Engineering Copilot - Setup Status
**Last Updated:** March 14, 2026  
**Status:** Local stack fully operational — all 5 containers healthy  
**Branch:** BT_deploy (all changes committed to this branch, not main)

---

## ✅ COMPLETED

### 1. Prerequisites Verified
- ✅ .NET SDK 9.0.312 installed
- ✅ Docker Desktop 29.1.3 installed  
- ✅ Docker Compose v2.40.3 installed
- ✅ Azure CLI 2.83.0 installed
- ✅ Azure cloud set to: **AzureUSGovernment**
- ✅ Logged in to Azure subscription: **Sub_Tomasiewicz_DEV**
- ✅ Git branch `BT_deploy` created and active

### 2. Service Principal Created
✅ **Service Principal created successfully on March 11, 2026**

**Purpose:** This SP allows the MCP server to:
- Query Azure resources (Discovery Agent)
- Deploy infrastructure (Infrastructure Agent)
- Assess compliance (Compliance Agent)
- Analyze costs (Cost Agent)

**Note:** Credentials stored in `.env` file (gitignored for security)

### 3. Configuration Files  
✅ **`.env` file created locally** (gitignored - contains secrets)
- Populated with Azure subscription details
- Service Principal credentials configured
- ⚠️ Azure OpenAI credentials pending

---

## ⚠️ NEXT STEPS

### Immediate: Create Azure OpenAI Resource

**Recommended Method: Azure Portal**
1. Go to https://portal.azure.us
2. Create → Azure OpenAI
3. Settings:
   - Resource group: Create new `rg-platform-engineering-copilot-dev`
   - Region: `USGov Virginia`
   - Name: `openai-pecop-dev-XXXX`
   - Pricing: `Standard S0`
4. After creation:
   - Keys and Endpoint → Copy Endpoint and KEY 1
   - Model deployments → Deploy `gpt-4` (latest turbo)
   - Deployment name: `gpt-4`, Capacity: 10 TPM
5. Update local `.env` file with:
   - `AZURE_OPENAI_ENDPOINT`
   - `AZURE_OPENAI_API_KEY`
   - `AZURE_OPENAI_DEPLOYMENT`

### After OpenAI Setup
### 4. Full Stack Verified (March 13–14, 2026)
✅ **All 5 containers running healthy on `docker-compose.mcp-chat-admin.yml`**

| Container | Port | Notes |
|-----------|------|-------|
| pec-sqlserver | 1433 | SQL Server 2022 |
| pec-mcp | 5100 | MCP agent server |
| pec-chat | 5001 | Chat UI (React + .NET) |
| pec-admin-api | 5050 | Admin REST API |
| pec-admin-client | 5003 | Admin Blazor UI |

**Start stack:**
```powershell
docker compose -f docker-compose.mcp-chat-admin.yml up -d
```

### 5. Chat UI Enhancements (March 14, 2026)
✅ **New features shipped to `platform-chat` container:**
- **Admin Panel** — gear ⚙️ icon opens full settings panel (replaces old info modal)
  - Appearance tab: dark mode toggle
  - Integrations tab: ADO Server URL, ADO Portal URL, GitHub Token + Org
  - About tab: app info & keyboard shortcuts
- **Dark mode** — Tailwind `darkMode: 'class'` strategy, persisted to localStorage
- **"PE Copilot"** label on assistant messages (was "Assistant")
- **Model selector** dropdown in chat toolbar (GPT-4o / GPT-4.1 / GPT-4o Mini)
- **File attachment** — paperclip now opens picker with `.pdf,.doc,.docx,.txt,.yaml,.yml,.json,.png,.jpg`
- **Settings persistence** — all settings stored in `localStorage['pe-copilot-settings']`

### 6. Bug Fixes (March 13, 2026)
✅ **Admin API crash loop fixed** — `DatabaseSeeder.cs` was calling `EnsureCreatedAsync()` which created tables without recording them in `__EFMigrationsHistory`. Removed the call so `MigrateAsync()` in Admin API works correctly.

✅ **Dockerfile React build fixed** — Chat `Dockerfile` had `SkipSpaPublish=true` which skipped React compilation inside Docker. Restructured to run `npm install && npm run build` explicitly before `dotnet publish`.

---

## ⚠️ NEXT STEPS

## 📁 KEY FILES

### Configuration (DO NOT COMMIT .env!)
- `.env` - **Local only**, gitignored (contains secrets)
- `.env.example` - Template for .env (safe to commit)
- `appsettings.example.json` - Full config template

### Infrastructure
- `infra/bicep/main.bicep` - Main deployment
- `infra/bicep/main.dev.bicepparam` - Dev parameters
- `docker-compose.mcp.yml` - Local MCP server

### Documentation
- `docs/GETTING-STARTED.md` - Setup guide
- `docs/DEPLOYMENT.md` - Deployment guide
- `docs/AGENTS.md` - Agent reference

---

## 🔒 SECURITY NOTES

- ✅ `.env` is gitignored - secrets stay local
- ✅ Service Principal has Contributor role (required for deployments)
- ✅ All work on `BT_deploy` branch - `main` branch unchanged
- ⚠️ Never commit `.env` or files with secrets to git
- ⚠️ Use Azure Key Vault for production deployments

---

## 🚀 QUICK COMMANDS

**Check current branch:**
```bash
git branch --show-current  # Should show: BT_deploy
```

**Start local development:**
```bash
dotnet build
docker-compose -f docker-compose.mcp.yml up -d
curl http://localhost:5100/health
```

**Deploy to Azure Gov:**
```bash
cd infra/bicep
az deployment group create \
  --resource-group rg-pecop-dev \
  --parameters main.dev.bicepparam
```

---

**Ready to continue?** Create the Azure OpenAI resource and update `.env`!
