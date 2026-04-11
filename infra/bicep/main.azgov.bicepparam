// =============================================================================
// Azure Government Arizona - Deployment Parameters
// Region:  usgovarizona
// RG:      rg-pecopilot
// Profile: Full stack (MCP + Chat + Admin API + Admin Client)
// =============================================================================
// Deploy command (run from repo root):
//   $az = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
//   & $az deployment group create `
//     --resource-group rg-pecopilot `
//     --template-file infra/bicep/main.bicep `
//     --parameters infra/bicep/main.azgov.bicepparam `
//     --parameters sqlAdminPassword='!A@S3d4f5g6h7j8k'
// =============================================================================
using 'main.bicep'

// Core Settings
param projectName = 'pecop'
param environment = 'dev'
param location    = 'usgovarizona'

// SQL Database - password passed at CLI, not stored here
param sqlAdminLogin    = 'platformadmin'
param sqlAdminPassword = ''

// Key Vault Admin - current signed-in user (wtomaz808 / vmadmin)
param keyVaultAdminObjectId = 'c699598f-aca9-4ca3-80a0-0a1516c5eb72'

// Container Deployment - ACI, full stack
param deploymentTarget  = 'aci'
param deployMcp         = true
param deployChat        = true
param deployAdminApi    = true
param deployAdminClient = true

// Adopt the ACR we already created (avoids suffix randomness + SKU issues in Gov)
param acrNameOverride        = 'pecopdevacr'
param acrLoginServerOverride = 'pecopdevacr.azurecr.us'

// Container Resources (dev sizing)
param imageTag       = 'latest'
param cpuCores       = 1
param memoryGB       = 2

// SQL SKU - Standard S1 for dev (slightly above S0 for reliable perf)
param sqlSku = 'S1'
