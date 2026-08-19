terraform {
  # >= 1.11 for S3 native state locking (use_lockfile in backend.tf).
  required_version = ">= 1.11.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}
