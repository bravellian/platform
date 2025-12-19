#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(cd -- "$(dirname -- "$0")/.." && pwd)
export UPDATE_SCHEMA_SNAPSHOT=1

dotnet test "$ROOT_DIR/tests/Bravellian.Platform.Tests/Bravellian.Platform.Tests.csproj" \
  --filter SchemaVersions_MatchSnapshot

git -C "$ROOT_DIR" diff -- src/Bravellian.Platform.Database/schema-versions.json
