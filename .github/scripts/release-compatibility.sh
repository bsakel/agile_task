#!/usr/bin/env bash
# Release compatibility checks (ADR-0008, ADR-0009, ADR-0020, ADR-0021 pull request pipeline, step 5).
#
# Baseline: the latest release tag (v*). Before the first release, the merge base with main (on main itself: the previous
# commit). Steps:
#   1. build the baseline and migrate an empty database with the baseline Migrator
#   2. generate the Marten patch of this commit against that schema (artifact) and check it is expand-only
#   3. migrate the same database forward with this commit's Migrator (upgrade path)
#   4. start the baseline Api against the NEW schema (rollback compatibility smoke) and export its OpenAPI document
#   5. start this commit's Api, export its OpenAPI document, and fail on breaking changes (oasdiff)
#
# Environment: PGHOST, PGPORT, PGUSER, PGPASSWORD (PostgreSQL to use), PSQL (psql command), OUT (artifact folder),
# WORK (short path for the baseline worktree; Windows builds fail beyond 260 characters).
set -euo pipefail

PGHOST=${PGHOST:-localhost}
PGPORT=${PGPORT:-5432}
PGUSER=${PGUSER:-postgres}
PGPASSWORD=${PGPASSWORD:-postgres}
PSQL=${PSQL:-psql}
OUT=$(mkdir -p "${OUT:-artifacts/release-compatibility}" && cd "${OUT:-artifacts/release-compatibility}" && pwd)
WORK=${WORK:-${RUNNER_TEMP:-/tmp}/opb-baseline}
OASDIFF_IMAGE=tufin/oasdiff@sha256:0286f138545a39010525df6c1bea67ffafacb384ef800effffa63bbd04718ce5
ROOT=$(pwd)
export PGHOST PGPORT PGUSER PGPASSWORD

baseline=$(git describe --tags --abbrev=0 --match 'v*' 2>/dev/null || true)
if [ -n "$baseline" ]; then
  echo "Baseline: release $baseline"
  echo "MIGRATIONS_RELEASE_TAG=$baseline" >> "${GITHUB_ENV:-/dev/null}"
else
  baseline=$(git merge-base HEAD origin/main)
  [ "$baseline" != "$(git rev-parse HEAD)" ] || baseline=$(git rev-parse HEAD^1)
  echo "::notice::No release tag yet; baseline is the main commit $baseline"
fi
echo "$baseline" > "$OUT/baseline.txt"

rm -rf "$WORK" && git worktree prune
git worktree add --detach "$WORK" "$baseline" > /dev/null
trap 'git worktree remove --force "$WORK" > /dev/null 2>&1 || true' EXIT

echo "::group::Build baseline and current"
dotnet build "$WORK/src/Tools/OrderPlatform.Migrator" -c Release -v quiet
dotnet build "$WORK/src/Host/OrderPlatform.Api" -c Release -v quiet
dotnet build src/Tools/OrderPlatform.Migrator -c Release -v quiet
dotnet build src/Host/OrderPlatform.Api -c Release -v quiet
echo "::endgroup::"

database="compat_$(date +%s)"
$PSQL -U "$PGUSER" -h "$PGHOST" -p "$PGPORT" -c "create database $database" > /dev/null
connection="Host=$PGHOST;Port=$PGPORT;Database=$database;Username=$PGUSER;Password=$PGPASSWORD"

run() { # directory, assembly, args...
  local directory=$1 assembly=$2
  shift 2
  (cd "$directory" && ConnectionStrings__orderplatform="$connection" Logging__LogLevel__Default=Warning dotnet "$assembly" "$@")
}

echo "::group::1. Baseline Migrator"
run "$WORK/src/Tools/OrderPlatform.Migrator/bin/Release/net10.0" OrderPlatform.Migrator.dll
echo "::endgroup::"

echo "::group::2. Marten patch against the baseline schema"
rm -f "$OUT/marten-patch.sql" "$OUT/marten-patch.drop.sql"
run "$ROOT/src/Host/OrderPlatform.Api/bin/Release/net10.0" OrderPlatform.Api.dll db-patch "$OUT/marten-patch.sql" -d marten://store/
if [ -f "$OUT/marten-patch.sql" ]; then
  echo "::notice::Marten schema changes found; review the marten-patch.sql artifact"
  MARTEN_PATCH_FILE="$OUT/marten-patch.sql" dotnet test --project tests/OrderPlatform.Migrations.Tests -c Release \
    --filter-method "*Generated_Marten_patch_is_expand_only"
else
  echo "No Marten schema changes compared with the baseline"
fi
echo "::endgroup::"

echo "::group::3. Upgrade: current Migrator on the baseline schema"
run "$ROOT/src/Tools/OrderPlatform.Migrator/bin/Release/net10.0" OrderPlatform.Migrator.dll
echo "::endgroup::"

start_api() { # directory, port, output file
  local directory=$1 port=$2 document=$3
  (cd "$directory" && ConnectionStrings__orderplatform="$connection" ASPNETCORE_ENVIRONMENT=Production ASPNETCORE_URLS="http://127.0.0.1:$port" \
    Authentication__Schemes__Bearer__Authority="http://127.0.0.1:9/realms/unused" Authentication__Schemes__Bearer__RequireHttpsMetadata=false \
    dotnet OrderPlatform.Api.dll > "$OUT/api-$port.log" 2>&1) &
  local pid=$!
  for _ in $(seq 1 90); do
    if [ "$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:$port/health/ready")" = "200" ]; then
      curl -s -o "$document" -w '%{http_code}' "http://127.0.0.1:$port/openapi/v1.json" > "$OUT/openapi-status-$port.txt" || true
      kill "$pid" 2> /dev/null || true
      wait "$pid" 2> /dev/null || true
      return 0
    fi
    sleep 1
  done
  kill "$pid" 2> /dev/null || true
  cat "$OUT/api-$port.log"
  return 1
}

echo "::group::4. Rollback compatibility: baseline Api on the new schema"
start_api "$WORK/src/Host/OrderPlatform.Api/bin/Release/net10.0" 5101 "$OUT/openapi-baseline.json" \
  || { echo "::error::The baseline Api does not start against this commit's schema; the release could not be rolled back (ADR-0009)"; exit 1; }
echo "Baseline Api is ready on the new schema"
echo "::endgroup::"

echo "::group::5. OpenAPI breaking changes"
start_api "$ROOT/src/Host/OrderPlatform.Api/bin/Release/net10.0" 5102 "$OUT/openapi.json" \
  || { echo "::error::The current Api did not start"; exit 1; }
if [ "$(cat "$OUT/openapi-status-5101.txt")" != "200" ]; then
  echo "::notice::The baseline publishes no OpenAPI document; breaking change check skipped"
  rm -f "$OUT/openapi-baseline.json"
else
  docker run --rm -v "$OUT:/specs" "$OASDIFF_IMAGE" breaking /specs/openapi-baseline.json /specs/openapi.json --fail-on ERR \
    | tee "$OUT/openapi-breaking-changes.txt"
fi
echo "::endgroup::"

$PSQL -U "$PGUSER" -h "$PGHOST" -p "$PGPORT" -c "drop database if exists $database with (force)" > /dev/null || true
