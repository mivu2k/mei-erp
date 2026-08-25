#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"; deploy_root="${MEIERP_DEPLOY_ROOT:-/opt/mei-erp}"
service="${MEIERP_SERVICE:-mei-erp.service}"; release="$deploy_root/releases/$(date -u +%Y%m%dT%H%M%S%N)"
mkdir -p "$release"; dotnet publish "$root/host/MeiErp.Host/MeiErp.Host.csproj" -c Release --no-restore --nologo -o "$release"
previous="$(readlink -f "$deploy_root/current" 2>/dev/null || true)"
ln -sfn "$release" "$deploy_root/current.new"; mv -Tf "$deploy_root/current.new" "$deploy_root/current"
printf '%s\n' "$previous" > "$deploy_root/previous"
if command -v systemctl >/dev/null && systemctl cat "$service" >/dev/null 2>&1; then
 sudo -n systemctl restart "$service"; for _ in {1..30}; do curl -fsS http://127.0.0.1:5090/health/ready >/dev/null && exit 0; sleep 1; done
 echo "health check failed; rolling back" >&2; "$root/ops/rollback.sh"; exit 1
fi
echo "published $release (service restart skipped)"
