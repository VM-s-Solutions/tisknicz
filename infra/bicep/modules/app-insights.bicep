// Application Insights module — telemetry sink for the OpenTelemetry
// Azure Monitor exporter wired in T-0014.

@description('Application Insights resource name.')
param appInsightsName string

@description('Log Analytics workspace name (App Insights is workspace-backed since 2020).')
param workspaceName string

param location string = resourceGroup().location

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: workspaceName
  location: location
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

output connectionString string = appInsights.properties.ConnectionString
output instrumentationKey string = appInsights.properties.InstrumentationKey
