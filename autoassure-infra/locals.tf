# Shared naming convention for all resources: <environment>-autoassure-<resource_name>-<resource_type>
# e.g. prod-autoassure-refresh-token-table
locals {
  name_prefix = "${var.environment}-autoassure"
}
