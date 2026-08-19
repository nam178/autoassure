variable "aws_region" {
  description = "AWS region to deploy resources into."
  type        = string
  default     = "ap-southeast-2"
}

variable "environment" {
  description = "Deployment environment name, used to prefix resource names (e.g. dev, prod)."
  type        = string
  default     = "prod"
}
