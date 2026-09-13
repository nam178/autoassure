# Stores hashed refresh tokens for autoassure-server's login-refresh flow.
# Schema must stay in sync with Models/RefreshToken.cs and
# Repositories/DynamoDbRefreshTokenRepository.cs in autoassure-server.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for a hashed-token table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "refresh_tokens" {
  name         = "${local.name_prefix}-refresh-token-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "RefreshTokenSecretHash"

  attribute {
    name = "RefreshTokenSecretHash"
    type = "S"
  }

  # ExpiresAt is stored as epoch seconds (see DynamoDbRefreshTokenRepository.cs)
  # so DynamoDB can use it directly for TTL-based cleanup. TTL deletion isn't
  # immediate (up to 48h), so the app still checks ExpiresAt on every read.
  ttl {
    attribute_name = "ExpiresAt"
    enabled        = true
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Stores AutoAssure user accounts, provisioned on Google sign-in.
# Schema must stay in sync with Models/User.cs and
# Repositories/DynamoDbUserRepository.cs in autoassure-server.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "users" {
  name         = "${local.name_prefix}-user-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "Id"

  attribute {
    name = "Id"
    type = "S"
  }

  attribute {
    name = "GoogleUserId"
    type = "S"
  }

  # Looks up a user by the Google account that signed them in. Eventually consistent
  # (GSIs don't support ConsistentRead) — see DynamoDbUserRepository.cs for how the
  # app handles that.
  global_secondary_index {
    name            = "GoogleUserIdIndex"
    hash_key        = "GoogleUserId"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Stores AutoAssure Organizations (multi-tenancy root). A personal Organization is
# auto-created for every user on first sign-in. Schema must stay in sync with
# Models/Organization.cs and Repositories/DynamoDbOrganizationRepository.cs.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "organizations" {
  name         = "${local.name_prefix}-organization-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "Id"

  attribute {
    name = "Id"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Join table linking Users to the Organizations they belong to. Schema must stay in
# sync with Models/OrganizationUser.cs and Repositories/DynamoDbOrganizationUserRepository.cs.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "organization_users" {
  name         = "${local.name_prefix}-organization-user-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId"
  range_key    = "UserId"

  attribute {
    name = "OrganizationId"
    type = "S"
  }

  attribute {
    name = "UserId"
    type = "S"
  }

  # Lists every Organization a User belongs to. Eventually consistent (GSIs don't
  # support ConsistentRead) — see DynamoDbOrganizationUserRepository.cs.
  global_secondary_index {
    name            = "UserIdIndex"
    hash_key        = "UserId"
    range_key       = "OrganizationId"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Stores AutoAssure Applications (systems under test), owned by an Organization.
# Schema must stay in sync with Models/Application.cs and Repositories/DynamoDbApplicationRepository.cs.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "applications" {
  name         = "${local.name_prefix}-application-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId"
  range_key    = "Id"

  attribute {
    name = "OrganizationId"
    type = "S"
  }

  attribute {
    name = "Id"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Stores Environments (deployment targets, e.g. "Staging"/"Production") for an
# Application. Variables are NOT stored here — see the environment_variables table
# below. Schema must stay in sync with Models/Environment.cs and
# Repositories/DynamoDbEnvironmentRepository.cs.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "environments" {
  name         = "${local.name_prefix}-environment-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_ApplicationId"
  range_key    = "Id"

  attribute {
    name = "OrganizationId_ApplicationId"
    type = "S"
  }

  attribute {
    name = "Id"
    type = "S"
  }

  attribute {
    name = "OrganizationId"
    type = "S"
  }

  # Point lookup by Id alone, for the flat PATCH /environments/{id} route (no
  # ApplicationId in the URL). Eventually consistent (GSIs don't support
  # ConsistentRead) — see DynamoDbEnvironmentRepository.cs.
  global_secondary_index {
    name            = "IdIndex"
    hash_key        = "OrganizationId"
    range_key       = "Id"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# One row per Environment variable key-value pair, so setting/deleting one variable
# never touches the others. Schema must stay in sync with Models/EnvironmentVariable.cs
# and Repositories/DynamoDbEnvironmentVariableRepository.cs.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "environment_variables" {
  name         = "${local.name_prefix}-environment-variable-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_EnvironmentId"
  range_key    = "Key"

  attribute {
    name = "OrganizationId_EnvironmentId"
    type = "S"
  }

  attribute {
    name = "Key"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Per-Application library of reusable Preconditions, referenced by Activities across
# Scenarios. Schema must stay in sync with Models/Precondition.cs and
# Repositories/DynamoDbPreconditionRepository.cs.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "preconditions" {
  name         = "${local.name_prefix}-precondition-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_ApplicationId"
  range_key    = "Id"

  attribute {
    name = "OrganizationId_ApplicationId"
    type = "S"
  }

  attribute {
    name = "Id"
    type = "S"
  }

  attribute {
    name = "OrganizationId"
    type = "S"
  }

  # Point lookup by Id alone, for the flat /preconditions/{id} routes. Eventually
  # consistent (GSIs don't support ConsistentRead) — see DynamoDbPreconditionRepository.cs.
  global_secondary_index {
    name            = "IdIndex"
    hash_key        = "OrganizationId"
    range_key       = "Id"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Per-Application library of reusable EvidenceDefinitions, referenced by Activities
# across Scenarios. Schema must stay in sync with Models/EvidenceDefinition.cs and
# Repositories/DynamoDbEvidenceDefinitionRepository.cs.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "evidence_definitions" {
  name         = "${local.name_prefix}-evidence-definition-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_ApplicationId"
  range_key    = "Id"

  attribute {
    name = "OrganizationId_ApplicationId"
    type = "S"
  }

  attribute {
    name = "Id"
    type = "S"
  }

  attribute {
    name = "OrganizationId"
    type = "S"
  }

  # Point lookup by Id alone, for the flat /evidence-definitions/{id} routes.
  # Eventually consistent (GSIs don't support ConsistentRead) — see
  # DynamoDbEvidenceDefinitionRepository.cs.
  global_secondary_index {
    name            = "IdIndex"
    hash_key        = "OrganizationId"
    range_key       = "Id"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Stores Scenarios (test cases). Their ordered Activities live in the separate
# activities table below. Schema must stay in sync with Models/Scenario.cs and
# Repositories/DynamoDbScenarioRepository.cs.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "scenarios" {
  name         = "${local.name_prefix}-scenario-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_ApplicationId"
  range_key    = "Id"

  attribute {
    name = "OrganizationId_ApplicationId"
    type = "S"
  }

  attribute {
    name = "Id"
    type = "S"
  }

  attribute {
    name = "OrganizationId"
    type = "S"
  }

  # Point lookup by Id alone, for the flat /scenarios/{id} routes. Eventually
  # consistent (GSIs don't support ConsistentRead) — see DynamoDbScenarioRepository.cs.
  global_secondary_index {
    name            = "IdIndex"
    hash_key        = "OrganizationId"
    range_key       = "Id"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Mapping table so "list Scenarios in folder X" is a plain, strongly consistent
# partition query instead of a filter expression. One row per Scenario, keyed by its
# current folder; kept in sync with the scenarios table in the same DynamoDB
# transaction. Schema must stay in sync with DynamoDbScenarioRepository.cs (the
# OrganizationId_ApplicationId_Folder attribute holds "{OrganizationId}_{ApplicationId}_{Folder}").
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "scenarios_by_folder" {
  name         = "${local.name_prefix}-scenarios-by-folder-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_ApplicationId_Folder"
  range_key    = "ScenarioId"

  attribute {
    name = "OrganizationId_ApplicationId_Folder"
    type = "S"
  }

  attribute {
    name = "ScenarioId"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Mapping table so "list Scenarios with tag X" is a plain, strongly consistent
# partition query instead of a filter expression. One row per (Scenario, tag) pair;
# kept in sync with the scenarios table in the same DynamoDB transaction. Schema must
# stay in sync with DynamoDbScenarioRepository.cs (the OrganizationId_ApplicationId_Tag
# attribute holds "{OrganizationId}_{ApplicationId}_{Tag}").
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "scenarios_by_tag" {
  name         = "${local.name_prefix}-scenarios-by-tag-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_ApplicationId_Tag"
  range_key    = "ScenarioId"

  attribute {
    name = "OrganizationId_ApplicationId_Tag"
    type = "S"
  }

  attribute {
    name = "ScenarioId"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Per-Scenario ordered list of Activities (test steps), split out from the scenarios
# table so an Activity's Precondition/EvidenceDefinition reference checks are scoped to
# that Activity's write instead of the whole Scenario's -- see Models/Activity.cs and
# Repositories/DynamoDbActivityRepository.cs.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "activities" {
  name         = "${local.name_prefix}-activity-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_ScenarioId"
  range_key    = "Id"

  attribute {
    name = "OrganizationId_ScenarioId"
    type = "S"
  }

  attribute {
    name = "Id"
    type = "S"
  }

  attribute {
    name = "OrganizationId"
    type = "S"
  }

  # Point lookup by Id alone, for the flat /activities/{id} routes. Eventually
  # consistent (GSIs don't support ConsistentRead) — see DynamoDbActivityRepository.cs.
  global_secondary_index {
    name            = "IdIndex"
    hash_key        = "OrganizationId"
    range_key       = "Id"
    projection_type = "ALL"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Stores a Run's four row shapes -- header, Environment snapshot, one row per Scenario snapshot, one
# row per status update -- all under the same OrganizationId_ApplicationId partition and a RowKey range
# key built as "<runId>", "<runId>#environment", "<runId>#scenario#<scenarioId>" or
# "<runId>#update#<seq, zero-padded to 12 digits>". Split this way so a single activity result write
# touches one small row instead of the whole Run -- see fix_run_design.md section 3. Schema must stay in
# sync with Models/Run.cs, Models/RunEnvironmentSnapshot.cs, Models/RunStatusUpdate.cs,
# Models/ActivityResult.cs and Repositories/DynamoDbMapper.Run.cs in autoassure-server.
#
# Deliberately carries no TTL: a Run's rows must be deleted together (header, Environment snapshot,
# every Scenario snapshot, the whole status-update log), and DynamoDB TTL evicts each row independently
# with no cross-item coordination or timing guarantee -- see the retention-mechanism discussion this
# replaced. There is currently no automated deletion path for old Runs; a dedicated reaper (deleting a
# Run's full row set in one TransactWriteItems, the same way TryCreateAsync writes it) is planned but not
# yet built.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "runs" {
  name         = "${local.name_prefix}-run-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_ApplicationId"
  range_key    = "RowKey"

  attribute {
    name = "OrganizationId_ApplicationId"
    type = "S"
  }

  attribute {
    name = "RowKey"
    type = "S"
  }

  # Only a header row carries HeaderId (its own RunId) -- body rows (Scenario snapshots, status
  # updates) do not. That sparseness is what keeps this index to one entry per Run.
  attribute {
    name = "HeaderId"
    type = "S"
  }

  # Answers List Runs by reading headers only, never a Scenario snapshot or status update row -- a
  # FilterExpression over the base partition would read (and charge for) every row of every Run.
  # Eventually consistent (GSIs don't support ConsistentRead) -- see DynamoDbRunRepository.cs. Projects
  # only what the Application's Runs panel shows, not the whole header, to keep this index small.
  global_secondary_index {
    name            = "RunHeaderIndex"
    hash_key        = "OrganizationId_ApplicationId"
    range_key       = "HeaderId"
    projection_type = "INCLUDE"
    non_key_attributes = [
      "Id",
      "Trigger",
      "Status",
      "StatusReason",
      "TotalActivityCount",
      "PassedActivityCount",
      "FailedActivityCount",
      "SkippedActivityCount",
      "CreatedAt",
      "StartedAt",
      "CompletedAt",
      "LastHeartbeatAt",
    ]
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Holds one row per Run that is currently Running, deleted the instant it ends. A real table, not a GSI
# off the runs table, specifically so ListRunningByApplicationAsync can read it with ConsistentRead:
# GSIs never support consistent reads, which would let a Run that just started be briefly missing from
# this list on refresh. Written and deleted in the exact same transaction as the runs table's own
# Status transition (see DynamoDbRunRepository.cs), so the two can never drift out of sync -- this table
# deliberately carries no TTL of its own, since that would reintroduce exactly that drift.
# Schema must stay in sync with Models/RunningRun.cs and Repositories/DynamoDbMapper.Run.cs in
# autoassure-server.
# trivy:ignore:AWS-0025 -- AWS-owned key is sufficient for this table at this stage;
# a customer-managed KMS key adds per-request cost and key-rotation overhead not justified yet.
# Revisit if compliance requirements change.
resource "aws_dynamodb_table" "running_runs" {
  name         = "${local.name_prefix}-running-run-table"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "OrganizationId_ApplicationId"
  range_key    = "RunId"

  attribute {
    name = "OrganizationId_ApplicationId"
    type = "S"
  }

  attribute {
    name = "RunId"
    type = "S"
  }

  point_in_time_recovery {
    enabled = var.environment == "prod"
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}
