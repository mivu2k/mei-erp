#!/usr/bin/env bash
set -euo pipefail

# Read-only discovery for the legacy MariaDB installation. This script never
# writes to either ERP. Supply the password through the environment so it does
# not appear in shell history or the process command line.
: "${LEGACY_MYSQL_PASSWORD:?Set LEGACY_MYSQL_PASSWORD for the read-only legacy account.}"

legacy_host="${LEGACY_MYSQL_HOST:-127.0.0.1}"
legacy_port="${LEGACY_MYSQL_PORT:-3306}"
legacy_user="${LEGACY_MYSQL_USER:-finance}"
output_dir="${1:-./artifacts/legacy-inventory}"
mkdir -p "$output_dir"

defaults_file="$(mktemp)"
trap 'rm -f "$defaults_file"' EXIT
chmod 600 "$defaults_file"
printf '[client]\nhost=%s\nport=%s\nuser=%s\npassword=%s\n' \
  "$legacy_host" "$legacy_port" "$legacy_user" "$LEGACY_MYSQL_PASSWORD" > "$defaults_file"

databases=(
  erp_identity finance_erp erp_hr erp_inventory erp_repair
  erp_gatepass erp_auto erp_ledger erp_tender
)

mysql_cmd=(mysql --defaults-extra-file="$defaults_file" --batch --raw --skip-column-names)
"${mysql_cmd[@]}" -e 'SELECT 1' >/dev/null

printf 'database,table_name,row_count\n' > "$output_dir/table-counts.csv"
printf 'database,table_name,column_name,ordinal_position,data_type,is_nullable,column_key\n' \
  > "$output_dir/columns.csv"

for database in "${databases[@]}"; do
  exists="$("${mysql_cmd[@]}" -e \
    "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name='${database}'")"
  if [[ "$exists" != "1" ]]; then
    printf '%s,__DATABASE_MISSING__,0\n' "$database" >> "$output_dir/table-counts.csv"
    continue
  fi

  while IFS= read -r table; do
    [[ -z "$table" ]] && continue
    # Names come only from information_schema, then are identifier-quoted.
    count="$("${mysql_cmd[@]}" -e "SELECT COUNT(*) FROM \`${database}\`.\`${table}\`")"
    printf '%s,%s,%s\n' "$database" "$table" "$count" >> "$output_dir/table-counts.csv"
  done < <("${mysql_cmd[@]}" -e \
    "SELECT table_name FROM information_schema.tables WHERE table_schema='${database}' AND table_type='BASE TABLE' ORDER BY table_name")

  "${mysql_cmd[@]}" -e \
    "SELECT table_schema,table_name,column_name,ordinal_position,data_type,is_nullable,column_key FROM information_schema.columns WHERE table_schema='${database}' ORDER BY table_name,ordinal_position" \
    | tr '\t' ',' >> "$output_dir/columns.csv"
done

sha256sum "$output_dir/table-counts.csv" "$output_dir/columns.csv" > "$output_dir/SHA256SUMS"
printf 'Legacy inventory written to %s\n' "$output_dir"

