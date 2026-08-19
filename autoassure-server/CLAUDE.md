# autoassure-server

Backend API project that serves autoassure-web.

## Structure

- `Controllers/`
- `Contracts/` — API request/response DTOs
- `Models/` — pure business domain models and functions.
- `Repositories/`
- `Services/`
- `Common/` — shared utilities
- `Repositories/` — persistence, currently DynamoDB-backed. AWS infra (tables, etc.) is provisioned
  via Terraform in `../autoassure-infra`, not created by the app at runtime.

## After coding

1. `dotnet build A2.Server.slnx`
2. `dotnet format A2.Server.slnx --verify-no-changes` (analyzer lint: unused imports, style; analyzers on via
   `EnableNETAnalyzers`+`EnforceCodeStyleInBuild` in csproj)
3. `dotnet jb inspectcode A2.Server.slnx -o=inspect.sarif.json --no-build --severity=WARNING` (ReSharper-grade
   inspections build/format/analyzers don't catch: never-instantiated types, unused positional properties, redundant
   using directives, etc. — requires `dotnet tool restore` once; see below)
4. Tests: ask first

(no need to format — `dotnet csharpier format .` runs automatically as a Claude Code hook)

Fix all errors/warnings from build and format. For `inspectcode` findings: fix real issues; for intentional not-yet-used
members, suppress.

## SDK generation

When any API changed (endpoint/DTO added/removed/modified) → ask user if they want to run `../scripts/generate-sdk.sh`
