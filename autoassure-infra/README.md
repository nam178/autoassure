# Deploying autoassure-infra

## Prerequisites

- Terraform >= 1.11.0 — install via [tfenv](https://github.com/tfutils/tfenv)
  (recommended, lets you match the version pinned in
  [versions.tf](versions.tf)):

  ```bash
  brew install tfenv
  tfenv install 1.11.0
  tfenv use 1.11.0
  ```

  Or install directly: `brew install terraform` (macOS), or see the
  [official install docs](https://developer.hashicorp.com/terraform/install) for
  other platforms. Verify with `terraform version`.

- [tflint](https://github.com/terraform-linters/tflint) (`brew install tflint`)
- AWS CLI configured with a `nam-admin` profile that has valid credentials (e.g.
  `aws sso login --profile nam-admin`)

> The commands below assume an AWS CLI profile named `nam-admin`. If yours is
> named differently, substitute it wherever `AWS_PROFILE=nam-admin` appears.

## Step 1: Init (first time only)

`terraform init` needs to authenticate to the S3 backend, so set `AWS_PROFILE`
first:

```bash
export AWS_PROFILE=nam-admin
terraform init
```

This connects to the shared S3 backend (see [backend.tf](backend.tf)) and
downloads providers. Re-run it whenever `backend.tf` or provider versions
change.

No need to set AWS region separately — it's set per-environment in each
`.tfvars` file (`aws_region`), currently `ap-southeast-2` for both dev and
prod. Pass a different `-var-file` to deploy elsewhere.

## Step 2: Select an environment

Environments are Terraform workspaces, one per `.tfvars` file
([dev.tfvars](dev.tfvars), [prod.tfvars](prod.tfvars)):

```bash
terraform workspace select -or-create dev   # or prod
```

This keeps dev and prod state separate within the same S3 bucket. `deploy.sh`
does this for you automatically — see Step 4.

## Step 3: Plan and apply

Use [deploy.sh](deploy.sh), which wraps the steps above plus `fmt` / `validate`
/ `tflint`:

```bash
./deploy.sh dev              # show the plan for dev
./deploy.sh dev apply        # review the plan, then apply after confirming
./deploy.sh prod apply       # same, for prod
```

You'll be asked to type the environment name to confirm before anything is
applied. Never run `terraform apply` directly without reviewing a plan first.

To run the steps manually instead:

```bash
export AWS_PROFILE=nam-admin
terraform fmt -recursive
terraform validate
tflint
terraform workspace select -or-create dev
terraform plan -var-file=dev.tfvars
terraform apply -var-file=dev.tfvars
```
