#!/usr/bin/env bash
set -u
SP="$(cd "$(dirname "$0")" && pwd)/out"
W=$(cygpath -w "$SP")
APP=/c/Users/bsakel/source/repos/agile_task/spikes/S1.Durability/bin/Debug/net10.0/S1.Durability.exe
psql() { docker exec -i spike-pg psql -U postgres -d spike -v ON_ERROR_STOP=1 -q "$@"; }
quiet() { grep -v -E "^Searching|^commands|PublicKeyToken|^for commands|^\s*$|\\|/  |\|" | tr -cd '\11\12\15\40-\176' | tail -${1:-4}; }
rm -rf "$SP" && mkdir -p "$SP"
psql -c "set client_min_messages to warning; drop schema if exists s1 cascade; drop schema if exists wolverine cascade; drop schema if exists s2 cascade;"
M="postgresql://localhost/spike/s1"; WV="postgresql://localhost/spike"

echo "=== 1. db-patch per database against EMPTY db (production config)"
SPIKE_AUTOCREATE=none "$APP" db-patch "$W\v1-marten.sql" -d "$M" 2>&1 | quiet
SPIKE_AUTOCREATE=none "$APP" db-patch "$W\v1-wolverine.sql" -d "$WV" 2>&1 | quiet
ls "$SP"
for x in "$SP"/v1-*.sql; do echo "--- $(basename $x): $(wc -l < $x) lines"; grep -o -i -E "create (table|or replace function|index|sequence)( if not exists)? [a-z0-9_.\"]+" "$x" | sort -u | head -20; done

echo "=== 2. apply patches externally (as the Migrator would)"
psql < "$SP/v1-marten.sql" && psql < "$SP/v1-wolverine.sql" && echo applied

echo "=== 3. db-assert"
SPIKE_AUTOCREATE=none "$APP" db-assert -d "$M" 2>&1 | quiet 3
SPIKE_AUTOCREATE=none "$APP" db-assert -d "$WV" 2>&1 | quiet 3

echo "=== 4. run production mode on applied schema"
SPIKE_AUTOCREATE=none timeout 30 "$APP" --urls http://localhost:5098 > "$SP/run-ok.log" 2>&1 &
sleep 12; curl -s -m 5 -X POST localhost:5098/s1/outbox/2 >/dev/null; sleep 4; echo "status: $(curl -s -m 5 localhost:5098/status | head -c 120)"
grep -E "^fail" -A2 "$SP/run-ok.log" | head -6; wait

echo "=== 5. schema change (index on Processed.Kind): db-patch"
SPIKE_AUTOCREATE=none SPIKE_INDEX=1 "$APP" db-patch "$W\v2-marten.sql" -d "$M" 2>&1 | quiet
ls "$SP"; for x in "$SP"/v2-*; do echo "--- $(basename $x):"; cat "$x"; done

echo "=== 6. production mode with v2 code but WITHOUT applying v2 (drift)"
SPIKE_AUTOCREATE=none SPIKE_INDEX=1 timeout 30 "$APP" --urls http://localhost:5098 > "$SP/run-drift.log" 2>&1 &
sleep 12; echo "outbox call: $(curl -s -m 5 -o /dev/null -w '%{http_code}' -X POST localhost:5098/s1/outbox/2)"; sleep 3
echo "status: $(curl -s -m 5 localhost:5098/status | head -c 120)"
grep -E "^(fail|warn)" -A2 "$SP/run-drift.log" | grep -v "^\s*at " | head -8; wait
