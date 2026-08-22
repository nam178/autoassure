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
