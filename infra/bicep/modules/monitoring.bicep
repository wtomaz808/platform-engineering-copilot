// Application Insights module for monitoring and telemetry
@description('Name of the Application Insights instance')
param applicationInsightsName string

@description('Name of the Log Analytics Workspace')
param logAnalyticsWorkspaceName string

@description('Location for all resources')
param location string = resourceGroup().location

@description('Environment name (dev, staging, prod)')
param environment string = 'dev'

@description('Application type')
@allowed([
  'web'
  'other'
])
param applicationType string = 'web'

@description('Retention in days')
@allowed([
  30
  60
  90
  120
  180
  270
  365
  550
  730
])
param retentionInDays int = environment == 'prod' ? 365 : 90

@description('Daily data cap in GB')
param dailyDataCapInGB int = environment == 'prod' ? 100 : 1

@description('Enable sampling')
param samplingPercentage int = environment == 'prod' ? 20 : 100

// Log Analytics Workspace
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
      disableLocalAuth: false
    }
    workspaceCapping: {
      dailyQuotaGb: dailyDataCapInGB
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
  tags: {
    Environment: environment
    Purpose: 'PlatformMonitoring'
  }
}

// Application Insights
resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: applicationType
  properties: {
    Application_Type: applicationType
    WorkspaceResourceId: logAnalyticsWorkspace.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    Request_Source: 'rest'
    SamplingPercentage: samplingPercentage
    RetentionInDays: retentionInDays
  }
  tags: {
    Environment: environment
    Purpose: 'PlatformMonitoring'
  }
}

// Output values
output applicationInsightsId string = applicationInsights.id
output applicationInsightsName string = applicationInsights.name
output instrumentationKey string = applicationInsights.properties.InstrumentationKey
output connectionString string = applicationInsights.properties.ConnectionString
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id
output logAnalyticsCustomerId string = logAnalyticsWorkspace.properties.customerId
@secure()
output logAnalyticsWorkspaceKey string = logAnalyticsWorkspace.listKeys().primarySharedKey
output logAnalyticsWorkspaceName string = logAnalyticsWorkspace.name
