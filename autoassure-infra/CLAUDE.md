Terraform for AutoAssure's AWS infra. Part of the [autoassure monorepo](../CLAUDE.md).

## Core practices

- Small, reviewable changes. Never `apply` without showing the `plan` first.
- Every resource name must follow `<environment>-autoassure-<resource_name>-<resource_type>` (e.g. `prod-autoassure-refresh-token-table`). Build it from `local.name_prefix` (`locals.tf`), not a hardcoded string or an overridable variable.
- Every variable and output must have a `description` (enforced by tflint).
- Pin provider versions in `versions.tf`; don't loosen constraints casually.
- No hardcoded secrets/ARNs/account IDs — use variables or data sources.
- State is remote; never edit `.tfstate` by hand or run `terraform apply` without knowing which workspace/backend you're targeting.

## After making changes

Run in order:

```
terraform fmt -recursive
terraform validate
tflint
```

Fix everything these report before considering the work done.
