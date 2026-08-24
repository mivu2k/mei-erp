#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"; work="$(mktemp -d /tmp/mei-erp-rehearsal.XXXXXX)"
verify_db="mei_erp_verify_rehearsal_$$"; staging_pid=""
cleanup(){ [[ -z "$staging_pid" ]] || kill "$staging_pid" 2>/dev/null || true; dropdb --if-exists --force -h "${MEIERP_DB_HOST:-127.0.0.1}" -U "${MEIERP_DB_USER:-meierp}" "$verify_db" >/dev/null; rm -rf "$work"; }
trap cleanup EXIT; export MEIERP_DEPLOY_ROOT="$work/deploy"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
export LD_LIBRARY_PATH="${LD_LIBRARY_PATH:-}:$HOME/.local/finance-erp-dev/pkg/usr/lib/x86_64-linux-gnu"
export PGPASSWORD="${MEIERP_DB_PASSWORD:-${PGPASSWORD:-}}"
backup="$($root/ops/backup.sh "$work/backups")"; MEIERP_KEEP_VERIFY_DB=1 "$root/ops/verify-restore.sh" "$backup" "$verify_db"
"$root/ops/deploy.sh"; first="$(readlink -f "$MEIERP_DEPLOY_ROOT/current")"; "$root/ops/deploy.sh"
[[ "$(readlink -f "$MEIERP_DEPLOY_ROOT/current")" != "$first" ]]; "$root/ops/rollback.sh"
[[ "$(readlink -f "$MEIERP_DEPLOY_ROOT/current")" == "$first" ]]; "$root/ops/monitor.sh"
ASPNETCORE_ENVIRONMENT=Staging ConnectionStrings__Platform="Host=${MEIERP_DB_HOST:-127.0.0.1};Database=$verify_db;Username=${MEIERP_DB_USER:-meierp};Password=${MEIERP_DB_PASSWORD:-}" \
  "$MEIERP_DEPLOY_ROOT/current/MeiErp.Host" --urls http://127.0.0.1:5190 >"$work/staging.log" 2>&1 & staging_pid=$!
for _ in {1..30}; do curl -fsS http://127.0.0.1:5190/health/ready >/dev/null 2>&1 && break; sleep 1; done
curl -fsS http://127.0.0.1:5190/health/ready >/dev/null; kill "$staging_pid"; wait "$staging_pid" || true; staging_pid=""
echo "rehearsal passed: backup, isolated restore, release, rollback, staging start, health"
