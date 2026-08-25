output "refresh_tokens_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:TableName (or an equivalent env var) to this value."
  value       = aws_dynamodb_table.refresh_tokens.name
}

output "refresh_tokens_table_arn" {
  description = "ARN of the refresh tokens table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.refresh_tokens.arn
}

output "users_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:UserTableName (or an equivalent env var) to this value."
  value       = aws_dynamodb_table.users.name
}

output "users_table_arn" {
  description = "ARN of the users table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.users.arn
}

output "organizations_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:OrganizationTableName to this value."
  value       = aws_dynamodb_table.organizations.name
}

output "organizations_table_arn" {
  description = "ARN of the organizations table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.organizations.arn
}

output "organization_users_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:OrganizationUserTableName to this value."
  value       = aws_dynamodb_table.organization_users.name
}

output "organization_users_table_arn" {
  description = "ARN of the organization users table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.organization_users.arn
}

output "applications_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:ApplicationTableName to this value."
  value       = aws_dynamodb_table.applications.name
}

output "applications_table_arn" {
  description = "ARN of the applications table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.applications.arn
}

output "environments_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:EnvironmentTableName to this value."
  value       = aws_dynamodb_table.environments.name
}

output "environments_table_arn" {
  description = "ARN of the environments table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.environments.arn
}

output "environment_variables_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:EnvironmentVariableTableName to this value."
  value       = aws_dynamodb_table.environment_variables.name
}

output "environment_variables_table_arn" {
  description = "ARN of the environment variables table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.environment_variables.arn
}

output "preconditions_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:PreconditionTableName to this value."
  value       = aws_dynamodb_table.preconditions.name
}

output "preconditions_table_arn" {
  description = "ARN of the preconditions table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.preconditions.arn
}

output "evidence_definitions_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:EvidenceDefinitionTableName to this value."
  value       = aws_dynamodb_table.evidence_definitions.name
}

output "evidence_definitions_table_arn" {
  description = "ARN of the evidence definitions table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.evidence_definitions.arn
}

output "scenarios_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:ScenarioTableName to this value."
  value       = aws_dynamodb_table.scenarios.name
}

output "scenarios_table_arn" {
  description = "ARN of the scenarios table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.scenarios.arn
}

output "scenarios_by_folder_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:ScenariosByFolderTableName to this value."
  value       = aws_dynamodb_table.scenarios_by_folder.name
}

output "scenarios_by_folder_table_arn" {
  description = "ARN of the scenarios-by-folder table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.scenarios_by_folder.arn
}

output "scenarios_by_tag_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:ScenariosByTagTableName to this value."
  value       = aws_dynamodb_table.scenarios_by_tag.name
}

output "scenarios_by_tag_table_arn" {
  description = "ARN of the scenarios-by-tag table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.scenarios_by_tag.arn
}

output "runs_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:RunTableName to this value."
  value       = aws_dynamodb_table.runs.name
}

output "runs_table_arn" {
  description = "ARN of the runs table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.runs.arn
}

output "tries_table_name" {
  description = "DynamoDB table name — set autoassure-server's DynamoDb:TryTableName to this value."
  value       = aws_dynamodb_table.tries.name
}

output "tries_table_arn" {
  description = "ARN of the tries table, e.g. for scoping an IAM policy to it."
  value       = aws_dynamodb_table.tries.arn
}
