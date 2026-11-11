// Log search alerts for NT.Blazor.ErrorBoundary telemetry in Application Insights.
// Deploy to the resource group that should own the alert rules.

@description('Resource ID of the Application Insights component that receives NT.Blazor.ErrorBoundary telemetry.')
param applicationInsightsId string

@description('Resource ID of the action group to notify. Leave empty to create the alerts without notifications.')
param actionGroupId string = ''

@description('Occurrences of one error fingerprint within 15 minutes that fire the error alert.')
param errorThreshold int = 10

@description('Stalled navigations or failed reconnections within 15 minutes that fire the client health alert.')
param clientHealthThreshold int = 5

param location string = resourceGroup().location

var actionGroups = empty(actionGroupId) ? [] : [actionGroupId]

resource errorSpike 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'blazor-error-fingerprint-spike'
  location: location
  properties: {
    displayName: 'Blazor error spike'
    description: 'A single Blazor error fingerprint occurred more often than the threshold within 15 minutes.'
    severity: 2
    enabled: true
    scopes: [applicationInsightsId]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: '''
exceptions
| extend Fingerprint = tostring(customDimensions['blazor.error.fingerprint'])
| where isnotempty(Fingerprint)
'''
          timeAggregation: 'Count'
          dimensions: [
            {
              name: 'Fingerprint'
              operator: 'Include'
              values: ['*']
            }
          ]
          operator: 'GreaterThan'
          threshold: errorThreshold
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: actionGroups
    }
  }
}

resource clientHealth 'Microsoft.Insights/scheduledQueryRules@2023-12-01' = {
  name: 'blazor-client-health'
  location: location
  properties: {
    displayName: 'Blazor stalled navigation or failed reconnection'
    description: 'Blazor navigations stalled before becoming interactive, or Blazor Server reconnections failed, more often than the threshold within 15 minutes.'
    severity: 3
    enabled: true
    scopes: [applicationInsightsId]
    evaluationFrequency: 'PT15M'
    windowSize: 'PT15M'
    criteria: {
      allOf: [
        {
          query: '''
customEvents
| where name in ("blazor.navigation.stalled", "blazor.reconnect.failed")
'''
          timeAggregation: 'Count'
          dimensions: [
            {
              name: 'name'
              operator: 'Include'
              values: ['*']
            }
          ]
          operator: 'GreaterThan'
          threshold: clientHealthThreshold
          failingPeriods: {
            numberOfEvaluationPeriods: 1
            minFailingPeriodsToAlert: 1
          }
        }
      ]
    }
    actions: {
      actionGroups: actionGroups
    }
  }
}
