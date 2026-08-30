# autoassure-server

Backend API project that serves autoassure-web.

## Multi-tenancy:

As AutoAssure is a multi-tenant SaaS. MUST include the tenant (OrganizationId) in ALL queries, operations and design.

## Structure

- `Controllers/`
- `Contracts/` — API request/response DTOs
- `Models/` — pure business domain models and functions.
- `Repositories/`
- `Services/`
- `Common/` — shared utilities
- `Repositories/` — storage layer

## After coding

Run verifications and fix errors:

1. `dotnet build A2.Server.slnx`
2. `dotnet format A2.Server.slnx --verify-no-changes`
3. `dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING`
4. Tests: ask first

(no need to format — `dotnet csharpier format .` runs automatically as a Claude Code hook)

Fix all errors/warnings from build and format. For `inspectcode` findings: fix real issues; for intentional not-yet-used
members, suppress.

## SDK generation

When any API changed (endpoint/DTO added/removed/modified) → ask user if they want to run `../scripts/generate-sdk.sh`
