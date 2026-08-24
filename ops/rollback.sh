#!/usr/bin/env bash
set -euo pipefail
deploy_root="${MEIERP_DEPLOY_ROOT:-/opt/mei-erp}"; service="${MEIERP_SERVICE:-mei-erp.service}"
previous="$(cat "$deploy_root/previous" 2>/dev/null || true)"
[[ -n "$previous" && -d "$previous" ]] || { echo "no previous release recorded" >&2; exit 1; }
current="$(readlink -f "$deploy_root/current")"; ln -sfn "$previous" "$deploy_root/current.new"; mv -Tf "$deploy_root/current.new" "$deploy_root/current"
printf '%s\n' "$current" > "$deploy_root/previous"
if command -v systemctl >/dev/null && systemctl cat "$service" >/dev/null 2>&1; then systemctl restart "$service"; fi
echo "rolled back to $previous"
