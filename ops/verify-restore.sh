#!/usr/bin/env bash
set -euo pipefail
backup="${1:?usage: verify-restore.sh BACKUP [VERIFY_DATABASE]}"
verify_db="${2:-mei_erp_verify_$(date -u +%Y%m%d%H%M%S)}"
db_host="${MEIERP_DB_HOST:-127.0.0.1}"; db_user="${MEIERP_DB_USER:-meierp}"
export PGPASSWORD="${MEIERP_DB_PASSWORD:-${PGPASSWORD:-}}"
[[ "$verify_db" == mei_erp_verify_* ]] || { echo "verification database must start mei_erp_verify_" >&2; exit 2; }
[[ -f "$backup" ]] || { echo "backup not found: $backup" >&2; exit 2; }
[[ ! -f "$backup.sha256" ]] || sha256sum --check "$backup.sha256"
cleanup(){ dropdb --if-exists --force -h "$db_host" -U "$db_user" "$verify_db" >/dev/null; }
[[ "${MEIERP_KEEP_VERIFY_DB:-0}" == 1 ]] || trap cleanup EXIT
cleanup; createdb -h "$db_host" -U "$db_user" "$verify_db"
pg_restore --exit-on-error --no-owner --no-acl -h "$db_host" -U "$db_user" -d "$verify_db" "$backup"
schemas="$(psql -h "$db_host" -U "$db_user" -d "$verify_db" -Atc "select count(*) from information_schema.schemata where schema_name in ('platform','finance','hr','inventory','repair','auto','gatepass','tender','ledger');")"
migrations="$(psql -h "$db_host" -U "$db_user" -d "$verify_db" -Atc 'select count(*) from platform.__migrations;')"
[[ "$schemas" == 9 ]] || { echo "restore has $schemas/9 application schemas" >&2; exit 1; }
[[ "$migrations" -gt 0 ]] || { echo "restore has no platform migrations" >&2; exit 1; }
echo "restore verified: 9 schemas, $migrations platform migrations"
