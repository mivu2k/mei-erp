#!/usr/bin/env bash
set -euo pipefail
base="${MEIERP_BASE_URL:-http://127.0.0.1:5090}"
curl -fsS --max-time 10 "$base/health/live" >/dev/null; curl -fsS --max-time 10 "$base/health/ready" >/dev/null
echo "MEI ERP healthy at $base"
