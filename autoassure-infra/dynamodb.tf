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

  attribute {
    name = "GoogleUserId"
    type = "S"
  }

  # Lets a future "revoke all sessions for this user" endpoint look up every
  # refresh token for a user. DynamoDB GSIs only ever support eventually
  # consistent reads, which is fine for that use case (not the per-request
  # token-validation hot path, which reads the base table by
  # RefreshTokenSecretHash instead, with ConsistentRead=true).
  global_secondary_index {
    name            = "GoogleUserIdIndex"
    hash_key        = "GoogleUserId"
    projection_type = "ALL"
  }

  # ExpiresAt is stored as epoch seconds (see DynamoDbRefreshTokenRepository.cs)
  # so DynamoDB can use it directly for TTL-based cleanup. TTL deletion isn't
  # immediate (up to 48h), so the app still checks ExpiresAt on every read.
  ttl {
    attribute_name = "ExpiresAt"
    enabled        = true
  }

  point_in_time_recovery {
    enabled = true
  }

  server_side_encryption {
    enabled = true
  }

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}
