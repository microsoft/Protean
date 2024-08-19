param appServiceName string
param location string = resourceGroup().location
param tags object = {}
param appServicePlanName string
param authClientId string
@secure()
param authClientSecret string
param authIssuerUri string
param serviceName string = 'backend'
param appSettings object = {}

module appServicePlan '../core/host/appserviceplan.bicep' = {
  name: 'appserviceplan-${serviceName}'
  params: {
    name: appServicePlanName
    location: location
    tags: tags
    sku: {
      name: 'B1'
      capacity: 1
    }
  }
}

module backend '../core/host/appservice.bicep' = {
  name: 'web-${serviceName}'
  params: {
    name: appServiceName
    location: location
    tags: union(tags, { 'azd-service-name': serviceName })
    appServicePlanId: appServicePlan.outputs.id
    scmDoBuildDuringDeployment: true
    managedIdentity: true
    authClientSecret: authClientSecret
    authClientId: authClientId
    authIssuerUri: authIssuerUri
    appSettings: appSettings
  }
}

output uri string = backend.outputs.uri
output identityPrincipalId string = backend.outputs.identityPrincipalId
