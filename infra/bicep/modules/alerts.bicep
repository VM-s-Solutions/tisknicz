// Alerting module (ADR 0023 §4 observability) — the Action Group + plain ARM metric alerts that
// make the App Insights / Azure Monitor telemetry actually page someone. NEW to Makables.
//
// Covers: per-site Http5xx + average latency over the five web hosts (Customer / Maker / Admin /
// Public + the Functions host), a single App Insights exceptions spike (one signal across all four
// APIs AND the Functions host), and the Postgres Flexible Server health trio (failed connections,
// CPU, storage). All alerts fan into one email Action Group.
//
// -------------------------------------------------------------------------------------------------
// WIRING (do in a later ticket — NOT this one):
//   Add to main.bicep, gated on a new `param alertEmail string = ''` so it only deploys when set:
//
//     module alerts 'modules/alerts.bicep' = if (!empty(alertEmail)) {
//       name: '${prefix}-alerts'
//       params: {
//         envSlug: envSlug            // 'dev' | 'prod' — drives severities/thresholds/windows
//         alertEmail: alertEmail      // the single ops inbox the Action Group notifies
//         siteNames: [                // deploy-time NAMES (strings), never module .outputs — the
//           customerApp.outputs.appName   // per-site for-loop needs a deploy-time array (BCP182).
//           makerApp.outputs.appName      // These outputs ARE deploy-time-known resource names, so
//           adminApp.outputs.appName      // they are safe to pass as the loop array.
//           publicApp.outputs.appName
//           functions.outputs.functionsAppName  // ADD this output to functions.bicep (it currently
//                                               // outputs storageAccountName/principalId only), OR
//                                               // pass the literal '${prefix}-functions'.
//         ]
//         postgresServerName: postgres.outputs.serverName
//         appInsightsName: '${prefix}-ai'   // matches main.bicep's appInsights module input
//       }
//     }
//
//   main.bicep already carries explicit resource ordering via the module DAG; the site/pg/AI
//   resources exist before this module's alerts reference them by name.
//
// TODO(infra): this module is UNWIRED. Before any production use:
//   1. Wire into main.bicep as above (gated on alertEmail).
//   2. Add `output functionsAppName string = functionsApp.name` to modules/functions.bicep so the
//      Functions host can be included in siteNames without a literal.
//   3. Validate with `az deployment group what-if` against the staging RG BEFORE the prod deploy.
//   Poison-queue depth is intentionally NOT here — queue metrics need diagnostic settings + a
//   scheduled-query alert; track separately.
//
// PARAMS:  envSlug, alertEmail, siteNames[], postgresServerName, appInsightsName, tags?
// OUTPUTS: actionGroupId (string) — future alert modules attach receivers/alerts to it.
// -------------------------------------------------------------------------------------------------

@description('Environment slug: dev | prod. Drives severities, thresholds, and evaluation windows (mirrors main.bicep).')
@allowed([
  'dev'
  'prod'
])
param envSlug string

@description('The single ops email the Action Group notifies.')
param alertEmail string

@description('Deploy-time resource NAMES of the web hosts to alert on: the four API App Services plus the Functions host. Pass names (strings), never module outputs used as runtime references — the per-site for-loop needs a deploy-time array.')
param siteNames array

@description('Deploy-time resource name of the PostgreSQL Flexible Server (mirrors modules/postgres.bicep serverName / output serverName).')
param postgresServerName string

@description('Deploy-time resource name of the Application Insights component (mirrors the appInsightsName var in main.bicep).')
param appInsightsName string

@description('Action Group resource name. main.bicep passes the Cleansia/CAF-style ag-makables-<region>-<env>; the default only covers direct module use.')
param actionGroupName string = 'ag-makables-${envSlug}'

@description('Resource tags applied to every alert resource.')
param tags object = {}

var isProd = envSlug == 'prod'

// Shared evaluation cadence — prod tight (page fast), dev wide (fewer, batched signals).
var windowSize = isProd ? 'PT5M' : 'PT15M'
var evaluationFrequency = isProd ? 'PT1M' : 'PT5M'

var http5xxSeverity = isProd ? 1 : 3
var http5xxThreshold = isProd ? 5 : 25
var latencySeverity = isProd ? 2 : 3
var responseTimeThresholdSeconds = 2
var exceptionsSeverity = isProd ? 2 : 3
var exceptionsThreshold = isProd ? 10 : 25

// -------------------------------------------------------------------------------------------------
// Action Group — the one email receiver every alert below fans into. Location is 'global' by design
// (action groups are not regional resources).
// -------------------------------------------------------------------------------------------------

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: actionGroupName
  location: 'global'
  tags: tags
  properties: {
    groupShortName: 'makables' // 12-char Azure limit — keep it env-agnostic
    enabled: true
    emailReceivers: [
      {
        name: 'ops-email'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// -------------------------------------------------------------------------------------------------
// Per-site alerts — Http5xx count + average response time over each of the five web hosts (the four
// API App Services + the Functions host, which is also a Microsoft.Web/sites resource). The site
// names already carry the makables-<env> prefix, so the alert names reuse them verbatim.
// -------------------------------------------------------------------------------------------------

resource http5xxAlerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [
  for siteName in siteNames: {
    name: 'alert-http5xx-${siteName}'
    location: 'global'
    tags: tags
    properties: {
      description: 'HTTP 5xx responses on ${siteName} exceeded ${http5xxThreshold} in ${windowSize}.'
      severity: http5xxSeverity
      enabled: true
      scopes: [resourceId('Microsoft.Web/sites', siteName)]
      evaluationFrequency: evaluationFrequency
      windowSize: windowSize
      criteria: {
        'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
        allOf: [
          {
            criterionType: 'StaticThresholdCriterion'
            name: 'Http5xx'
            metricNamespace: 'Microsoft.Web/sites'
            metricName: 'Http5xx'
            operator: 'GreaterThan'
            threshold: http5xxThreshold
            timeAggregation: 'Total'
          }
        ]
      }
      actions: [
        {
          actionGroupId: actionGroup.id
        }
      ]
    }
  }
]

resource latencyAlerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [
  for siteName in siteNames: {
    name: 'alert-latency-${siteName}'
    location: 'global'
    tags: tags
    properties: {
      description: 'Average HTTP response time on ${siteName} exceeded ${responseTimeThresholdSeconds}s over ${windowSize}.'
      severity: latencySeverity
      enabled: true
      scopes: [resourceId('Microsoft.Web/sites', siteName)]
      evaluationFrequency: evaluationFrequency
      windowSize: windowSize
      criteria: {
        'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
        allOf: [
          {
            criterionType: 'StaticThresholdCriterion'
            name: 'HttpResponseTime'
            metricNamespace: 'Microsoft.Web/sites'
            metricName: 'HttpResponseTime'
            operator: 'GreaterThan'
            threshold: responseTimeThresholdSeconds
            timeAggregation: 'Average'
          }
        ]
      }
      actions: [
        {
          actionGroupId: actionGroup.id
        }
      ]
    }
  }
]

// -------------------------------------------------------------------------------------------------
// App Insights exceptions spike — ONE alert over the shared component, so it covers server-side
// exceptions from all four APIs (Customer / Maker / Admin / Public) AND the Functions host in a
// single signal.
// -------------------------------------------------------------------------------------------------

resource exceptionsAlert 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-exceptions-makables-${envSlug}'
  location: 'global'
  tags: tags
  properties: {
    description: 'Server exceptions across the APIs + Functions exceeded ${exceptionsThreshold} in ${windowSize}.'
    severity: exceptionsSeverity
    enabled: true
    scopes: [resourceId('Microsoft.Insights/components', appInsightsName)]
    evaluationFrequency: evaluationFrequency
    windowSize: windowSize
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'ExceptionsCount'
          metricNamespace: 'microsoft.insights/components'
          metricName: 'exceptions/count'
          operator: 'GreaterThan'
          threshold: exceptionsThreshold
          timeAggregation: 'Count'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// -------------------------------------------------------------------------------------------------
// Postgres Flexible Server health — failed connections (any failure pages in prod), CPU saturation,
// and storage headroom (auto-grow is Enabled in postgres.bicep, but a human still needs to react
// before the ceiling / SKU cap).
// -------------------------------------------------------------------------------------------------

var postgresAlertRules = [
  {
    shortName: 'connfailed'
    metricName: 'connections_failed'
    timeAggregation: 'Total'
    threshold: isProd ? 0 : 10
    severity: isProd ? 1 : 3
    description: 'Failed connections on the PostgreSQL server ${postgresServerName}.'
  }
  {
    shortName: 'cpu'
    metricName: 'cpu_percent'
    timeAggregation: 'Average'
    threshold: 90
    severity: isProd ? 2 : 3
    description: 'CPU above 90% on the PostgreSQL server ${postgresServerName}.'
  }
  {
    shortName: 'storage'
    metricName: 'storage_percent'
    timeAggregation: 'Average'
    threshold: 85
    severity: isProd ? 2 : 3
    description: 'Storage above 85% on the PostgreSQL server ${postgresServerName}.'
  }
]

resource postgresAlerts 'Microsoft.Insights/metricAlerts@2018-03-01' = [
  for rule in postgresAlertRules: {
    name: 'alert-pg-${rule.shortName}-makables-${envSlug}'
    location: 'global'
    tags: tags
    properties: {
      description: rule.description
      severity: rule.severity
      enabled: true
      scopes: [resourceId('Microsoft.DBforPostgreSQL/flexibleServers', postgresServerName)]
      evaluationFrequency: evaluationFrequency
      windowSize: windowSize
      criteria: {
        'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
        allOf: [
          {
            criterionType: 'StaticThresholdCriterion'
            name: rule.metricName
            metricNamespace: 'Microsoft.DBforPostgreSQL/flexibleServers'
            metricName: rule.metricName
            operator: 'GreaterThan'
            threshold: rule.threshold
            timeAggregation: rule.timeAggregation
          }
        ]
      }
      actions: [
        {
          actionGroupId: actionGroup.id
        }
      ]
    }
  }
]

@description('The Action Group resource id — future alert modules (e.g. a poison-queue scheduled query) attach to it.')
output actionGroupId string = actionGroup.id
