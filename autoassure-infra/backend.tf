# Backend config can't reference variables/locals, so the bucket/region are
# hardcoded here. This is the shared state bucket for autoassure-infra across
# all environments; dev/prod are kept separate via terraform workspaces
# (see deploy.sh), which namespace the state key automatically.
terraform {
  backend "s3" {
    bucket       = "autoassure-terraform-state-729937450399-ap-southeast-2-an"
    key          = "autoassure-infra/terraform.tfstate"
    region       = "ap-southeast-2"
    use_lockfile = true
  }
}
