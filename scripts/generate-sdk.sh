#!/usr/bin/env bash
#
# generate-sdk.sh
#
# What this does:
#   Builds autoassure-server, exports its OpenAPI spec, and regenerates the
#   TypeScript client in autoassure-server-sdk/ (an axios-based client that
#   autoassure-web imports as a local "file:" dependency).
#
#   Steps:
#     1. `dotnet build` autoassure-server (emits the OpenAPI JSON at build
#        time via the Microsoft.Extensions.ApiDescription.Server MSBuild
#        target — no need to run the server).
#     2. Copy the spec to autoassure-server-sdk/openapi.json.
#     3. Regenerate autoassure-server-sdk/src/Api.ts from that spec via
#        swagger-typescript-api (axios-based client, exposes the underlying
#        AxiosInstance for later interceptor/retry configuration).
#     4. Compile the SDK (tsc) to autoassure-server-sdk/dist/.
#
#   This always does a full regeneration — it does not try to detect
#   whether the API actually changed. Since it's a manual step, just run it
#   whenever you've changed the server's API surface and want the SDK/web
#   project to pick it up.
#
# When to run this:
#   - After adding/changing/removing an endpoint (or its request/response
#     types) in autoassure-server.
#   - Before building/running autoassure-web for the first time, or after
#     pulling server changes from git (see autoassure-web/README.md).
#
# Usage:
#   bash autoassure/scripts/generate-sdk.sh
#
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVER_DIR="$ROOT_DIR/autoassure-server"
SDK_DIR="$ROOT_DIR/autoassure-server-sdk"
SPEC_SCRATCH_DIR="$SDK_DIR/.spec"

echo "==> Building autoassure-server (generates OpenAPI spec)"
dotnet build "$SERVER_DIR" --nologo -v minimal

SPEC_FILE="$(find "$SPEC_SCRATCH_DIR" -maxdepth 1 -name '*.json' | head -n 1)"
if [ -z "$SPEC_FILE" ]; then
  echo "error: no OpenAPI spec found in $SPEC_SCRATCH_DIR after build" >&2
  exit 1
fi

echo "==> Using spec: $SPEC_FILE"
cp "$SPEC_FILE" "$SDK_DIR/openapi.json"

echo "==> Regenerating TypeScript client (swagger-typescript-api, axios)"
npx --yes swagger-typescript-api generate \
  -p "$SDK_DIR/openapi.json" \
  -o "$SDK_DIR/src" \
  -n Api.ts \
  --axios \
  --single-http-client \
  --clean-output

echo "==> Compiling SDK (tsc)"
(cd "$SDK_DIR" && npm install --no-audit --no-fund && npm run build)

echo "==> Done. Generated:"
echo "    $SDK_DIR/openapi.json"
echo "    $SDK_DIR/src/Api.ts"
echo "    $SDK_DIR/dist/"
