#!/usr/bin/env bash
# Local dev environment for MEI ERP.
#
#   ./dev.sh up        start the app        -> http://localhost:5090
#   ./dev.sh down      stop it
#   ./dev.sh status    is it running?
#   ./dev.sh db        open a SQL shell
#   ./dev.sh reset     drop and recreate the database (destructive, asks first)
#   ./dev.sh test      run the whole suite
#
# PostgreSQL is expected to be installed system-wide and already running.
# Bootstrap of the role and database is a one-time sudo step - see README.

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
HOST_PROJECT="$ROOT/host/MeiErp.Host"
PORT=5090
APP_NAME="MeiErp.Host"

DB_NAME="${MEIERP_DB:-mei_erp}"
DB_USER="${MEIERP_DB_USER:-meierp}"
DB_HOST="${MEIERP_DB_HOST:-127.0.0.1}"

# .NET needs these: ICU is unpacked locally rather than system-installed, and
# the runtime hard-fails at startup without LD_LIBRARY_PATH pointing at it.
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
export LD_LIBRARY_PATH="${LD_LIBRARY_PATH:-}:$HOME/.local/finance-erp-dev/pkg/usr/lib/x86_64-linux-gnu"

app_running() { pgrep -x "$APP_NAME" >/dev/null 2>&1; }

db_running() { pg_isready -h "$DB_HOST" -q 2>/dev/null; }

require_db() {
    if ! db_running; then
        echo "PostgreSQL is not accepting connections on $DB_HOST:5432." >&2
        echo "Start it with:  sudo systemctl start postgresql" >&2
        exit 1
    fi
}

seed_config() {
    local target="$HOST_PROJECT/appsettings.Development.json"
    local example="$HOST_PROJECT/appsettings.Development.json.example"
    if [ ! -f "$target" ] && [ -f "$example" ]; then
        cp "$example" "$target"
        echo "seeded appsettings.Development.json from the example"
        echo "  -> put the database password in it; the file is gitignored"
    fi
}

case "${1:-up}" in
    up)
        require_db
        seed_config
        if app_running; then
            echo "already running -> http://localhost:$PORT"
            exit 0
        fi
        echo "starting on http://localhost:$PORT"
        cd "$HOST_PROJECT"
        dotnet run --urls "http://localhost:$PORT"
        ;;

    down)
        # Never pkill -f "dotnet run": that pattern also matches the shell an
        # agent is running in, and takes the session down with it.
        if app_running; then
            pkill -x "$APP_NAME" && echo "stopped"
        else
            echo "not running"
        fi
        ;;

    status)
        db_running && echo "database: up" || echo "database: down"
        app_running && echo "app:      up -> http://localhost:$PORT" || echo "app:      down"
        ;;

    db)
        require_db
        psql -U "$DB_USER" -h "$DB_HOST" -d "$DB_NAME"
        ;;

    reset)
        require_db
        read -rp "Drop and recreate '$DB_NAME'? Everything in it is lost. [y/N] " reply
        [[ "$reply" == "y" || "$reply" == "Y" ]] || { echo "cancelled"; exit 0; }
        psql -U "$DB_USER" -h "$DB_HOST" -d postgres \
             -c "DROP DATABASE IF EXISTS $DB_NAME;" \
             -c "CREATE DATABASE $DB_NAME OWNER $DB_USER;"
        echo "recreated. run ./dev.sh up to apply migrations."
        ;;

    test)
        cd "$ROOT"
        dotnet test --nologo
        ;;

    *)
        echo "usage: ./dev.sh {up|down|status|db|reset|test}" >&2
        exit 1
        ;;
esac
