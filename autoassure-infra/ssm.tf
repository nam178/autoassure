# Publishes resource names to SSM Parameter Store so autoassure-server and the
# workers load them automatically (Program.cs calls AddSystemsManager on
# Ssm:ParameterPath). A parameter at <path>/DynamoDb/UserTableName becomes the
# config key DynamoDb:UserTableName. Names must match DynamoDbOptions.cs.
locals {
  # e.g. /AutoAssure/Prod — must match Ssm:ParameterPath in appsettings.json.
  ssm_path = "/AutoAssure/${title(var.environment)}"

  ssm_parameters = {
    "DynamoDb/RefreshTokenTableName"        = aws_dynamodb_table.refresh_tokens.name
    "DynamoDb/UserTableName"                = aws_dynamodb_table.users.name
    "DynamoDb/OrganizationTableName"        = aws_dynamodb_table.organizations.name
    "DynamoDb/OrganizationUserTableName"    = aws_dynamodb_table.organization_users.name
    "DynamoDb/ApplicationTableName"         = aws_dynamodb_table.applications.name
    "DynamoDb/EnvironmentTableName"         = aws_dynamodb_table.environments.name
    "DynamoDb/EnvironmentVariableTableName" = aws_dynamodb_table.environment_variables.name
    "DynamoDb/PreconditionTableName"        = aws_dynamodb_table.preconditions.name
    "DynamoDb/EvidenceDefinitionTableName"  = aws_dynamodb_table.evidence_definitions.name
    "DynamoDb/ScenarioTableName"            = aws_dynamodb_table.scenarios.name
    "DynamoDb/ScenariosByFolderTableName"   = aws_dynamodb_table.scenarios_by_folder.name
    "DynamoDb/ScenariosByTagTableName"      = aws_dynamodb_table.scenarios_by_tag.name
    "DynamoDb/ActivityTableName"            = aws_dynamodb_table.activities.name
    "DynamoDb/RunTableName"                 = aws_dynamodb_table.runs.name
    "DynamoDb/RunningRunTableName"          = aws_dynamodb_table.running_runs.name
    "WorkerQueue/QueueUrl"                  = aws_sqs_queue.worker.url
  }
}

resource "aws_ssm_parameter" "config" {
  for_each = local.ssm_parameters

  name  = "${local.ssm_path}/${each.key}"
  type  = "String"
  value = each.value

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}
