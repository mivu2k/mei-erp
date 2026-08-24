#!/usr/bin/env bash
set -euo pipefail
backup_dir="${1:-/var/backups/mei-erp}"; db_name="${MEIERP_DB:-mei_erp}"
db_host="${MEIERP_DB_HOST:-127.0.0.1}"; db_user="${MEIERP_DB_USER:-meierp}"
export PGPASSWORD="${MEIERP_DB_PASSWORD:-${PGPASSWORD:-}}"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"; mkdir -p "$backup_dir"
target="$backup_dir/${db_name}_${stamp}.dump"; partial="$target.partial"
trap 'rm -f "$partial"' EXIT
pg_dump -h "$db_host" -U "$db_user" -d "$db_name" -Fc -Z9 --no-owner --no-acl -f "$partial"
pg_restore --list "$partial" >/dev/null; mv "$partial" "$target"
sha256sum "$target" > "$target.sha256"; chmod 600 "$target" "$target.sha256"; echo "$target"
