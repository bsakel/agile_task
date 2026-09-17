#!/usr/bin/env bash
# Phase 0 spike S6: online DDL behaviour on PostgreSQL 17 (lock queue, lock_timeout, rewrites, NOT VALID, CONCURRENTLY).
set -u
p()  { docker exec -i spike-pg psql -U postgres -d spike -q -t -A "$@"; }
ms() { local s=$(date +%s%N); "$@" >/dev/null 2>&1; local rc=$?; echo "$(( ($(date +%s%N)-s)/1000000 ))ms rc=$rc"; }

p -c "set client_min_messages to warning; drop schema if exists s6 cascade; create schema s6;
      create table s6.orders(id bigint primary key, address text);
      insert into s6.orders select g, 'Street '||g||', 1234 AB' from generate_series(1,2000000) g;
      analyze s6.orders;"
echo "server: $(p -c 'show server_version')"; echo "rows: $(p -c 'select count(*) from s6.orders')"

echo; echo "=== 1. Lock queue: long reader + ALTER without lock_timeout blocks everyone"
p -c "begin; select count(*) from s6.orders; select pg_sleep(8); commit;" >/dev/null 2>&1 &   # holds ACCESS SHARE 8s
sleep 1
( echo "ALTER (no timeout): $(ms p -c 'alter table s6.orders add column note1 text')" ) &
sleep 1
echo "plain SELECT while ALTER waits: $(ms p -c 'select id from s6.orders where id = 1')"
wait

echo; echo "=== 2. Same with lock_timeout = 2s"
p -c "begin; select count(*) from s6.orders; select pg_sleep(8); commit;" >/dev/null 2>&1 &
sleep 1
( echo "ALTER (lock_timeout 2s): $(ms p -c "set lock_timeout = '2s'; alter table s6.orders add column note2 text")" ) &
sleep 3
echo "plain SELECT after ALTER gave up: $(ms p -c 'select id from s6.orders where id = 1')"
wait

echo; echo "=== 3. ADD COLUMN variants on 2M rows (rewrite = relfilenode changes)"
f() { p -c "select pg_relation_filenode('s6.orders')"; }
for ddl in "add column c_null text" "add column c_const text not null default 'x'" "add column c_volatile uuid default gen_random_uuid()"; do
  before=$(f); t=$(ms p -c "alter table s6.orders $ddl"); after=$(f)
  echo "$ddl -> $t rewrite=$([ "$before" != "$after" ] && echo YES || echo no)"
done

echo; echo "=== 4. Constraints: validated vs NOT VALID + VALIDATE"
echo "add CHECK validated:        $(ms p -c "alter table s6.orders add constraint ck1 check (id > 0)")"
echo "add CHECK NOT VALID:        $(ms p -c "alter table s6.orders add constraint ck2 check (id > 0) not valid")"
echo "VALIDATE CONSTRAINT:        $(ms p -c "alter table s6.orders validate constraint ck2")"
echo "lock taken by VALIDATE:     $(p -c "begin; alter table s6.orders add constraint ck3 check (id > 0) not valid; commit; begin; alter table s6.orders validate constraint ck3; select mode from pg_locks where relation = 's6.orders'::regclass and granted and pid = pg_backend_pid() order by 1; commit;" | tr '\n' ' ')"
echo "SET NOT NULL (plain scan):  $(ms p -c "alter table s6.orders alter column address set not null")"
p -c "alter table s6.orders alter column address drop not null" >/dev/null
echo "SET NOT NULL w/ valid CHECK (address is not null): $(ms p -c "alter table s6.orders add constraint ck_addr check (address is not null) not valid; alter table s6.orders validate constraint ck_addr; select 1")"
echo "   ...then SET NOT NULL:    $(ms p -c "alter table s6.orders alter column address set not null")"

echo; echo "=== 5. CREATE INDEX: blocking vs CONCURRENTLY, and inside a transaction"
echo "lock taken by CREATE INDEX: $(p -c "begin; create index ix_a on s6.orders(address); select string_agg(mode, ',') from pg_locks where relation = 's6.orders'::regclass and granted and pid = pg_backend_pid(); rollback;")"
echo "CREATE INDEX CONCURRENTLY:  $(ms p -c "create index concurrently ix_b on s6.orders(address)")"
echo "CONCURRENTLY in a tx block: $(p -c "begin; create index concurrently ix_c on s6.orders(id, address); commit;" 2>&1 | head -1)"

echo; echo "=== 6. Batched backfill (split address) — 100k rows per batch"
p -c "alter table s6.orders add column street text, add column post_code text" >/dev/null
s=$(date +%s); batches=0
while :; do
  n=$(p -c "with b as (select id from s6.orders where street is null limit 100000)
            update s6.orders o set street = split_part(o.address, ',', 1), post_code = trim(split_part(o.address, ',', 2))
            from b where o.id = b.id" -c "select 1" | head -1)
  left=$(p -c "select count(*) from s6.orders where street is null"); batches=$((batches+1))
  [ "$left" = "0" ] && break
done
echo "backfilled 2M rows in $batches batches, $(( $(date +%s)-s ))s total; each batch is its own short transaction"
