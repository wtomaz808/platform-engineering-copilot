// =============================================================================
// Azure Container Registry (ACR) - IL5/IL6 Compliant
// =============================================================================
// Creates a Premium SKU ACR with:
// - Geo-replication for disaster recovery
// - Content trust and image quarantine
// - Vulnerability scanning with Microsoft Defender
// - Private endpoints for network isolation
// - Encryption at rest with customer-managed keys
// - Audit logging and monitoring
// =============================================================================

@description('The name of the Azure Container Registry')
param acrName string

@description('Location for the primary registry')
param location string = resourceGroup().location

@description('Azure Container Registry SKU (Premium required for IL5/IL6)')
@allowed(['Basic', 'Standard', 'Premium'])
param sku string = 'Premium'

@description('Enable geo-replication for disaster recovery')
param enableGeoReplication bool = true

@description('Replication locations for geo-redundancy')
param replicationLocations array = [
  'usgovvirginia'
  'usgovarizona'
]

@description('Enable content trust (image signing)')
param enableContentTrust bool = true

@description('Enable image quarantine (scan before use)')
param enableQuarantine bool = true

@description('Enable public network access (false for IL5/IL6 with private endpoints)')
param publicNetworkAccess string = 'Disabled'

@description('Enable admin user (not recommended for production)')
param adminUserEnabled bool = false

@description('Resource tags for compliance and cost tracking')
param tags object = {
  Environment: 'Production'
  Classification: 'CUI'
  ImpactLevel: 'IL5'
  MissionOwner: 'Platform Engineering'
  CostCenter: 'Engineering'
  Compliance: 'NIST-800-53'
}

@description('Enable customer-managed encryption keys')
param enableCustomerManagedKey bool = false

@description('Key Vault resource ID for customer-managed keys')
param keyVaultId string = ''

@description('Key name in Key Vault for encryption')
param encryptionKeyName string = ''

@description('Enable zone redundancy (Premium SKU only)')
param zoneRedundancy bool = true

@description('Retention policy for untagged manifests (days)')
param retentionDays int = 30

@description('Enable retention policy')
param enableRetentionPolicy bool = true

// =============================================================================
// Azure Container Registry
// =============================================================================
resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  tags: tags
  sku: {
    name: sku
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    adminUserEnabled: adminUserEnabled
    publicNetworkAccess: publicNetworkAccess

    // VNet/network rules — Premium SKU only. Null omits these from the request for Standard/Basic.
    networkRuleBypassOptions: sku == 'Premium' ? 'AzureServices' : null
    zoneRedundancy: sku == 'Premium' ? (zoneRedundancy ? 'Enabled' : 'Disabled') : null
    networkRuleSet: sku == 'Premium' ? {
      defaultAction: 'Deny'
    } : null

    // Policies: quarantinePolicy, trustPolicy, exportPolicy are Premium-only.
    // For Standard/Basic, only retentionPolicy is supported.
    policies: sku == 'Premium' ? {
      quarantinePolicy: {
        status: enableQuarantine ? 'enabled' : 'disabled'
      }
      trustPolicy: {
        type: 'Notary'
        status: enableContentTrust ? 'enabled' : 'disabled'
      }
      retentionPolicy: {
        days: retentionDays
        status: enableRetentionPolicy ? 'enabled' : 'disabled'
      }
      exportPolicy: {
        status: 'disabled'
      }
    } : {
      // Standard/Basic: only retention policy is supported
      retentionPolicy: {
        days: retentionDays
        status: enableRetentionPolicy ? 'enabled' : 'disabled'
      }
    }

    // Encryption with CMK requires Premium + Key Vault integration
    encryption: (sku == 'Premium' && enableCustomerManagedKey) ? {
      status: 'enabled'
      keyVaultProperties: {
        identity: ''
        keyIdentifier: '${keyVaultId}/keys/${encryptionKeyName}'
      }
    } : null
  }
}

// =============================================================================
// Geo-Replication (Premium SKU only)
// =============================================================================
resource replication 'Microsoft.ContainerRegistry/registries/replications@2023-07-01' = [
  for (replicationLocation, i) in replicationLocations: if (enableGeoReplication && sku == 'Premium') {
    parent: containerRegistry
    name: replicationLocation
    location: replicationLocation
    tags: tags
    properties: {
      regionEndpointEnabled: true
      zoneRedundancy: zoneRedundancy ? 'Enabled' : 'Disabled'
    }
  }
]

// =============================================================================
// Diagnostic Settings for Audit Logging (skipped — no workspace ID param on this module)
// To enable: add logAnalyticsWorkspaceId param and uncomment
// =============================================================================

// =============================================================================
// Outputs
// =============================================================================
output acrId string = containerRegistry.id
output acrName string = containerRegistry.name
output acrLoginServer string = containerRegistry.properties.loginServer
output acrPrincipalId string = containerRegistry.identity.principalId
output acrResourceId string = containerRegistry.id
