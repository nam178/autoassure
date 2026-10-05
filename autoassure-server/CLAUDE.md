# autoassure-server

Backend API project that serves autoassure-web. To understand business entities, see ../docs/about.md.

## Multi-tenancy:

As AutoAssure is a multi-tenant SaaS. MUST include the tenant (OrganizationId) in ALL queries, operations and design.

## Structure

Split into 5 assemblies:

**A2.Server.Common.** Shared utilities and types.
**A2.Server.UserManagement.** Deal with authentication, authorization, user and organizations.
**A2.Server.Engine.** The heart, business logic of AutoAssure. Test execution engine.
**A2.Server.WebApi.** Expose WebAPIs for everything else.
**A2.Server.Workers.** Background tasks. Consume jobs from an SQS.

## After coding

Run verifications and fix errors:

1. dotnet build A2.Server.slnx
2. dotnet format A2.Server.slnx
3. dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING
4. Unit tests (A2.Server.UnitTests)
5. Integration Tests (A2.Server.Tests)

Fix all errors/warnings from build and format. For `inspectcode` findings: fix real issues; for intentional not-yet-used
members, suppress.

## SDK generation

When any API changed (endpoint/DTO added/removed/modified) → ask user if they want to run `../scripts/generate-sdk.sh`
