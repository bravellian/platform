#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(cd -- "$(dirname -- "$0")/.." && pwd)
export UPDATE_SCHEMA_SNAPSHOT=1

echo "Running schema snapshot test (SchemaVersions_MatchSnapshot)..."
if ! dotnet test "$ROOT_DIR/tests/Bravellian.Platform.Tests/Bravellian.Platform.Tests.csproj" \
  --filter SchemaVersions_MatchSnapshot; then
  echo "Schema snapshot test failed. The snapshot file may not have been updated." >&2
  exit 1
fi

echo "Schema snapshot test completed successfully. Checking for changes in schema-versions.json..."
git -C "$ROOT_DIR" diff --quiet -- src/Bravellian.Platform.SqlServer/Database/schema-versions.json >/dev/null 2>&1 || diff_exit=$?
diff_exit=${diff_exit:-0}

if [ "$diff_exit" -eq 0 ]; then
  echo "No changes detected in src/Bravellian.Platform.SqlServer/Database/schema-versions.json."
  exit 0
elif [ "$diff_exit" -eq 1 ]; then
  echo "Schema snapshot differs from the committed version. Showing diff:"
  git -C "$ROOT_DIR" diff -- src/Bravellian.Platform.SqlServer/Database/schema-versions.json
  exit 1
else
  echo "git diff failed with exit code $diff_exit while checking schema-versions.json." >&2
  exit "$diff_exit"
fi
