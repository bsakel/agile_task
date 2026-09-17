#!/usr/bin/env bash
# Compose smoke test (ADR-0021 pull request pipeline, step 7): builds both images, starts the local stack and checks that
# the Migrator ran first, the Api started from pre-generated handler code, and the API works with a real Keycloak token.
# Until Phase 2 adds orders, the diagnostics endpoint stands in for "submit and read an order".
set -euo pipefail

compose=(docker compose -f deploy/docker-compose.yml)
api=http://localhost:8081
keycloak=http://localhost:8080/realms/orderplatform/protocol/openid-connect/token

fail() {
  echo "::error::$1"
  "${compose[@]}" ps -a || true
  "${compose[@]}" logs --no-color migrator api keycloak | tail -n 300 || true
  exit 1
}

wait_for() { # url, seconds
  for _ in $(seq 1 "$2"); do
    if [ "$(curl -s -o /dev/null -w '%{http_code}' "$1")" = "200" ]; then return 0; fi
    sleep 1
  done
  return 1
}

"${compose[@]}" config --quiet
"${compose[@]}" up -d --build

wait_for "$api/health/ready" 240 || fail "Api did not become ready"

migrator_exit=$(docker inspect "$("${compose[@]}" ps -a -q migrator)" --format '{{.State.ExitCode}}')
[ "$migrator_exit" = "0" ] || fail "Migrator exited with $migrator_exit"
echo "✔ Migrator completed (exit 0) before the Api started"

"${compose[@]}" logs --no-color api | grep -q "Using pre-generated Wolverine HandlerRegistry" \
  || fail "Api did not start with pre-generated (static) Wolverine code"
echo "✔ Api runs pre-generated handler code (static mode)"

token=$(curl -sf "$keycloak" -d grant_type=client_credentials -d client_id=acme-erp -d client_secret=acme-erp-dev-secret \
  | sed -E 's/.*"access_token":"([^"]+)".*/\1/') || fail "No token from Keycloak"

status=$(curl -s -o /dev/null -w '%{http_code}' -X POST "$api/v1/_diagnostics/echo" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: smoke-anonymous' -d '{"message":"smoke"}')
[ "$status" = "401" ] || fail "Request without token returned $status, expected 401"
echo "✔ Request without token rejected (401)"

key="smoke-$(date +%s)"
first=$(curl -sf -X POST "$api/v1/_diagnostics/echo" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $key" -d '{"message":"smoke"}') || fail "Authorized request failed"
replay_headers=$(curl -s -D - -o /dev/null -X POST "$api/v1/_diagnostics/echo" -H "Authorization: Bearer $token" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d '{"message":"smoke"}')
echo "$first" | grep -q '"accountId":"0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c01"' || fail "Unexpected response: $first"
echo "$replay_headers" | grep -qi '^Idempotent-Replayed: true' || fail "Repeated request was not replayed"
echo "✔ Authorized request succeeded and its repeat was replayed from the idempotency store"

curl -sf "$api/openapi/v1.json" > /dev/null || fail "OpenAPI document not served"
echo "✔ OpenAPI document served"
