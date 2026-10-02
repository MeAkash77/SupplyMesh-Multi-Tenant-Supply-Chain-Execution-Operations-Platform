// az deployment group create -g supplymesh-rg -f main.bicep -p sqlAdminPassword=<secret>
param location string = resourceGroup().location
@secure() param sqlAdminPassword string
var suffix = uniqueString(resourceGroup().id)

resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = { name: 'smacr${suffix}', location: location, sku: { name: 'Standard' }, properties: { adminUserEnabled: false } }

resource aks 'Microsoft.ContainerService/managedClusters@2024-02-01' = {
  name: 'supplymesh-aks', location: location, identity: { type: 'SystemAssigned' }
  properties: {
    dnsPrefix: 'supplymesh'
    agentPoolProfiles: [{ name: 'system', count: 3, vmSize: 'Standard_D4s_v5', mode: 'System', availabilityZones: ['1','2','3'], enableAutoScaling: true, minCount: 3, maxCount: 8 }]
    addonProfiles: { azureKeyvaultSecretsProvider: { enabled: true } }
    securityProfile: { workloadIdentity: { enabled: true } }
    oidcIssuerProfile: { enabled: true }
    networkProfile: { networkPlugin: 'azure', networkPolicy: 'azure' }
  }
}

// Event Hubs Standard+ exposes a Kafka-compatible endpoint (port 9093) -> same Confluent.Kafka client code as local
resource ehns 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: 'supplymesh-ns-${suffix}', location: location, sku: { name: 'Standard', tier: 'Standard', capacity: 2 }, properties: { kafkaEnabled: true, minimumTlsVersion: '1.2' }
}
resource poEvents 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = { parent: ehns, name: 'po.events', properties: { partitionCount: 6, messageRetentionInDays: 3 } }
resource poDlq 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = { parent: ehns, name: 'po.events.dlq', properties: { partitionCount: 2, messageRetentionInDays: 7 } }

resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = { name: 'smsql${suffix}', location: location, properties: { administratorLogin: 'smadmin', administratorLoginPassword: sqlAdminPassword, minimalTlsVersion: '1.2', publicNetworkAccess: 'Disabled' } }
resource sqldb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = { parent: sql, name: 'SupplyMesh', location: location, sku: { name: 'GP_Gen5', tier: 'GeneralPurpose', capacity: 4 }, properties: { zoneRedundant: true } }

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = { name: 'smkv${suffix}', location: location, properties: { sku: { family: 'A', name: 'standard' }, tenantId: subscription().tenantId, enableRbacAuthorization: true, enablePurgeProtection: true } }

resource law 'Microsoft.OperationalInsights/workspaces@2023-09-01' = { name: 'supplymesh-law', location: location, properties: { retentionInDays: 30 } }
resource ai 'Microsoft.Insights/components@2020-02-02' = { name: 'supplymesh-ai', location: location, kind: 'web', properties: { Application_Type: 'web', WorkspaceResourceId: law.id } }

// AKS pulls from ACR with managed identity (AcrPull)
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: acr, name: guid(acr.id, aks.id, 'acrpull')
  properties: { roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d'), principalId: aks.properties.identityProfile.kubeletidentity.objectId, principalType: 'ServicePrincipal' }
}
