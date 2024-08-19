param accountName string
param principalIds array = []

module roleDefinition './core/database/cosmos/sql/cosmos-sql-role-def.bicep' = {
  name: 'cosmos-sql-role-definition'
  params: {
    accountName: accountName
  }
}

// We need batchSize(1) here because sql role assignments have to be done sequentially
@batchSize(1)
module userRole './core/database/cosmos/sql/cosmos-sql-role-assign.bicep' = [for principalId in principalIds: if (!empty(principalId)) {
  name: 'cosmos-sql-user-role-${uniqueString(principalId)}'
  params: {
    accountName: accountName
    roleDefinitionId: roleDefinition.outputs.id
    principalId: principalId
  }
}]
