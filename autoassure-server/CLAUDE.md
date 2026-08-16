# autoassure-server

ASP.NET Core (net10.0) API. OpenAPI spec generated at build time, consumed by `autoassure-server-sdk`.

## Structure

- `Controllers/` — endpoints
- `Models/` — core pure models, pure business logic functions.
- `Repositories/` — data access
- `Services/` — business logic
- `Common/` — shared utilities

## After coding

1. `dotnet build`
2. `dotnet format --verify-no-changes` (analyzer lint: unused imports, style; analyzers on via `EnableNETAnalyzers`+
   `EnforceCodeStyleInBuild` in csproj)
3. Tests: ask first

(no need formatting, its done automatically via `dotnet csharpier format .` which runs automatically as a Claude Code
hook)

Fix all errors/warnings.

## SDK regen

API changed (endpoint/DTO added/removed/modified) → ask before running `../scripts/generate-sdk.sh` (rebuilds server,
overwrites SDK, reinstalls package).
