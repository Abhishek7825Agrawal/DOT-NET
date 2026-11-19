#!/usr/bin/env bash
set -euo pipefail

PROJECT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_DIR"
APP_URL="http://localhost:5000"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "Shelfwise needs the .NET 10 SDK. Install it, then run this file again."
    exit 1
fi

# If a previous launch is still running, reuse it instead of failing with
# Kestrel's "address already in use" error.
if command -v curl >/dev/null 2>&1 && curl -sS --max-time 2 -o /dev/null "$APP_URL/" 2>/dev/null; then
    echo "Shelfwise is already running at $APP_URL"
    exit 0
fi

# Honor an explicitly supplied SQL Server connection. Otherwise use the local
# SQLite fallback so the app can run without a database server.
if [[ -n "${ConnectionStrings__DefaultConnection:-}" ]]; then
    export DatabaseProvider="SqlServer"
elif [[ "${DatabaseProvider:-}" =~ ^[Ss][Qq][Ll][Ss][Ee][Rr][Vv][Ee][Rr]$ ]]; then
    echo "DatabaseProvider is SqlServer but ConnectionStrings__DefaultConnection is missing."
    exit 1
else
    export DatabaseProvider="Sqlite"
    export ConnectionStrings__DefaultConnection="Data Source=library.db"
fi
export ASPNETCORE_ENVIRONMENT="Development"

echo "Starting Shelfwise with provider: $DatabaseProvider"
echo "Open $APP_URL after the server starts."
export ASPNETCORE_URLS="$APP_URL"
dotnet run --no-launch-profile
