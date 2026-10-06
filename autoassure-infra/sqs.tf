# Generic queue for background work. autoassure-server publishes messages
# (ExecuteRun, DeleteScenario, DeleteApplication) and a worker pulls them.
# Message contracts live in AsyncProcessing/ in autoassure-server; the message
# kind travels as the "kind" SQS message attribute.

# Holds messages that failed too many times, so they can be inspected and replayed.
resource "aws_sqs_queue" "worker_dead_letter" {
  name                      = "${local.name_prefix}-worker-dead-letter-queue"
  message_retention_seconds = 1209600 # 14 days, the SQS maximum
  sqs_managed_sse_enabled   = true

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

resource "aws_sqs_queue" "worker" {
  name = "${local.name_prefix}-worker-queue"

  # Must be longer than the slowest message handler, or SQS hands the same
  # message to a second worker while the first is still working on it.
  visibility_timeout_seconds = 900    # 15 minutes
  message_retention_seconds  = 345600 # 4 days
  receive_wait_time_seconds  = 20     # long polling: fewer empty receives, lower cost
  sqs_managed_sse_enabled    = true

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.worker_dead_letter.arn
    maxReceiveCount     = 5
  })

  tags = {
    Environment = var.environment
    Project     = "autoassure"
  }
}

# Only the worker queue may use the dead-letter queue.
resource "aws_sqs_queue_redrive_allow_policy" "worker_dead_letter" {
  queue_url = aws_sqs_queue.worker_dead_letter.id

  redrive_allow_policy = jsonencode({
    redrivePermission = "byQueue"
    sourceQueueArns   = [aws_sqs_queue.worker.arn]
  })
}
