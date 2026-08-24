#!/usr/bin/env bash
set -euo pipefail

: "${MEIERP_DB_PASSWORD:?Set MEIERP_DB_PASSWORD for the rebuild PostgreSQL account.}"
db_host="${MEIERP_DB_HOST:-127.0.0.1}"
db_port="${MEIERP_DB_PORT:-5432}"
db_name="${MEIERP_DB_NAME:-mei_erp}"
db_user="${MEIERP_DB_USER:-meierp}"
output_dir="${1:-./artifacts/rebuild-inventory}"
mkdir -p "$output_dir"

export PGPASSWORD="$MEIERP_DB_PASSWORD"
schemas="'platform','finance','hr','inventory','repair','gatepass','auto','ledger','tender'"

psql -X -h "$db_host" -p "$db_port" -U "$db_user" -d "$db_name" -At -F, \
  -c "SELECT table_schema,table_name FROM information_schema.tables WHERE table_schema IN (${schemas}) AND table_type='BASE TABLE' ORDER BY table_schema,table_name" \
  > "$output_dir/tables.csv"

printf 'schema,table_name,row_count\n' > "$output_dir/table-counts.csv"
while IFS=, read -r schema table; do
  count="$(psql -X -h "$db_host" -p "$db_port" -U "$db_user" -d "$db_name" -At \
    -c "SELECT COUNT(*) FROM \"${schema}\".\"${table}\"")"
  printf '%s,%s,%s\n' "$schema" "$table" "$count" >> "$output_dir/table-counts.csv"
done < "$output_dir/tables.csv"

psql -X -h "$db_host" -p "$db_port" -U "$db_user" -d "$db_name" -At -F, \
  -c "SELECT table_schema,table_name,column_name,ordinal_position,data_type,is_nullable FROM information_schema.columns WHERE table_schema IN (${schemas}) ORDER BY table_schema,table_name,ordinal_position" \
  > "$output_dir/columns.csv"

sha256sum "$output_dir/table-counts.csv" "$output_dir/columns.csv" > "$output_dir/SHA256SUMS"
printf 'Rebuild inventory written to %s\n' "$output_dir"

