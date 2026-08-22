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
