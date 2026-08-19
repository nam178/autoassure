#!/usr/bin/env bash
#
# Deploy autoassure-infra to AWS.
#
# Usage:
#   ./deploy.sh <dev|prod> [plan|apply] [--skip-checks]
#
# Examples:
#   ./deploy.sh dev              # show the plan for dev
#   ./deploy.sh dev apply        # review + apply the plan for dev
#   ./deploy.sh prod apply       # review + apply the plan for prod
#
# What this does:
#   1. Uses the "nam-admin" AWS profile (override by exporting AWS_PROFILE
#      yourself before running this script).
#   2. Runs `terraform fmt -recursive`, `terraform validate`, and `tflint`.
#      CLAUDE.md requires these after any change to this repo; they run in a
#      few seconds and catch typos/drift before they reach a plan against
#      real infra, so they run by default here too. Pass --skip-checks to
#      skip them for a quick throwaway plan.
#   3. Selects (creating if needed) the terraform workspace matching the
#      environment, so dev and prod state stay separate within the shared
#      S3 backend (see backend.tf).
#   4. Runs `terraform plan -var-file=<env>.tfvars` and always shows it.
#   5. On `apply`, requires you to type the environment name to confirm
#      before applying. Never applies without the plan being shown first.
#
# Prerequisites:
#   - Valid credentials for the "nam-admin" profile, e.g.:
#       aws sso login --profile nam-admin
#   - `terraform init` has already been run in this directory (initializes
#     the S3 backend and downloads providers).

set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"

ENVIRONMENT="${1:-}"
ACTION="${2:-plan}"
SKIP_CHECKS=false

for arg in "$@"; do
  if [[ "$arg" == "--skip-checks" ]]; then
    SKIP_CHECKS=true
  fi
done

if [[ "$ENVIRONMENT" != "dev" && "$ENVIRONMENT" != "prod" ]]; then
  echo "Usage: $0 <dev|prod> [plan|apply] [--skip-checks]" >&2
  exit 1
fi

if [[ "$ACTION" != "plan" && "$ACTION" != "apply" ]]; then
  echo "Usage: $0 <dev|prod> [plan|apply] [--skip-checks]" >&2
  exit 1
fi

export AWS_PROFILE="${AWS_PROFILE:-nam-admin}"
echo "Using AWS profile: $AWS_PROFILE"

VAR_FILE="${ENVIRONMENT}.tfvars"
if [[ ! -f "$VAR_FILE" ]]; then
  echo "Missing $VAR_FILE" >&2
  exit 1
fi

if [[ "$SKIP_CHECKS" != true ]]; then
  echo "==> terraform fmt -recursive"
  terraform fmt -recursive

  echo "==> terraform validate"
  terraform validate

  echo "==> tflint"
  tflint
else
  echo "Skipping fmt/validate/tflint (--skip-checks)"
fi

echo "==> terraform workspace select $ENVIRONMENT"
terraform workspace select -or-create "$ENVIRONMENT"

PLAN_FILE="${ENVIRONMENT}.tfplan"
echo "==> terraform plan -var-file=$VAR_FILE"
terraform plan -var-file="$VAR_FILE" -out="$PLAN_FILE"

if [[ "$ACTION" == "apply" ]]; then
  read -r -p "Apply the plan above to '$ENVIRONMENT' using profile '$AWS_PROFILE'? Type the environment name to confirm: " CONFIRM
  if [[ "$CONFIRM" != "$ENVIRONMENT" ]]; then
    echo "Confirmation did not match '$ENVIRONMENT'. Aborting."
    rm -f "$PLAN_FILE"
    exit 1
  fi
  terraform apply "$PLAN_FILE"
fi

rm -f "$PLAN_FILE"
