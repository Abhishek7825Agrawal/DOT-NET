#!/usr/bin/env bash
set -Eeuo pipefail

PROJECT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="$PROJECT_DIR/LibraryManagementSystem.csproj"
APP_URL="${SHELFWISE_URL:-http://localhost:5000}"
cd "$PROJECT_DIR"

fail() {
    printf 'Shelfwise startup error: %s\n' "$*" >&2
    exit 1
}

if [[ ! -f "$PROJECT_FILE" ]]; then
    fail "Could not find LibraryManagementSystem.csproj beside start.sh."
fi

if ! command -v dotnet >/dev/null 2>&1; then
    fail ".NET 10 SDK is required. Install it, then run this file again."
fi

if ! SDK_VERSION="$(dotnet --version 2>/dev/null)"; then
    fail "The installed .NET SDK does not satisfy this project's global.json requirement."
fi
if [[ "$SDK_VERSION" != 10.* ]]; then
    fail "This project requires .NET SDK 10.x; dotnet selected $SDK_VERSION."
fi

if [[ "$APP_URL" != http://* && "$APP_URL" != https://* ]]; then
    fail "SHELFWISE_URL must start with http:// or https:// (got '$APP_URL')."
fi

# Reuse Shelfwise only when its own branded page responds. If something else
# owns the URL, stop with a useful message instead of claiming it is our app.
if command -v curl >/dev/null 2>&1; then
    if RESPONSE="$(curl -sSL --max-time 2 -w $'\n%{http_code}' "$APP_URL/" 2>/dev/null)"; then
        HTTP_STATUS="${RESPONSE##*$'\n'}"
        PAGE="${RESPONSE%$'\n'*}"
        if [[ "$HTTP_STATUS" =~ ^2[0-9][0-9]$ && "$PAGE" == *Shelfwise* ]]; then
            printf 'Shelfwise is already running at %s\n' "$APP_URL"
            exit 0
        fi
        fail "Something is already responding at $APP_URL, but it does not look like Shelfwise. Stop it or set SHELFWISE_URL to another address."
    fi
else
    printf 'Note: curl is unavailable, so the launcher cannot detect an existing server at %s.\n' "$APP_URL" >&2
fi

# Select a provider from explicit configuration. If only a connection string
# is supplied, infer its provider; reject ambiguous strings instead of silently
# connecting to a different database.
PROVIDER="${DatabaseProvider:-}"
CONNECTION_STRING="${ConnectionStrings__DefaultConnection:-}"

if [[ -z "$PROVIDER" ]]; then
    if [[ -z "$CONNECTION_STRING" ]]; then
        PROVIDER="Sqlite"
    elif [[ "$CONNECTION_STRING" =~ (^|;)[[:space:]]*(Server|Initial[[:space:]]Catalog|Integrated[[:space:]]Security|Trusted_Connection)[[:space:]]*= ]]; then
        PROVIDER="SqlServer"
    elif [[ "$CONNECTION_STRING" =~ (^|;)[[:space:]]*Filename[[:space:]]*= || "$CONNECTION_STRING" =~ (^|;)[[:space:]]*Data[[:space:]]Source[[:space:]]*=[[:space:]]*(:memory:|[^;]*\.(db|sqlite|sqlite3)(;|$)) ]]; then
        PROVIDER="Sqlite"
    else
        fail "Cannot infer the database provider from ConnectionStrings__DefaultConnection. Set DatabaseProvider=SqlServer or Sqlite."
    fi
fi

case "${PROVIDER,,}" in
    sqlserver)
        [[ -n "$CONNECTION_STRING" ]] || fail "DatabaseProvider is SqlServer but ConnectionStrings__DefaultConnection is missing."
        export DatabaseProvider="SqlServer"
        ;;
    sqlite)
        if [[ -z "$CONNECTION_STRING" ]]; then
            CONNECTION_STRING="Data Source=$PROJECT_DIR/library.db"
        elif [[ ! "$CONNECTION_STRING" =~ (^|;)[[:space:]]*(Data[[:space:]]Source|Filename)[[:space:]]*= ]]; then
            fail "DatabaseProvider is Sqlite, but ConnectionStrings__DefaultConnection does not look like a SQLite connection string."
        fi
        export DatabaseProvider="Sqlite"
        export ConnectionStrings__DefaultConnection="$CONNECTION_STRING"
        ;;
    *)
        fail "Unsupported DatabaseProvider '$PROVIDER'. Use SqlServer or Sqlite."
        ;;
esac

export ASPNETCORE_ENVIRONMENT="Development"
export ASPNETCORE_URLS="$APP_URL"

printf 'Starting Shelfwise with provider: %s\n' "$DatabaseProvider"
printf 'Open %s after the server starts. Stop it with Ctrl+C.\n' "$APP_URL"
exec dotnet run --no-launch-profile --project "$PROJECT_FILE" --urls "$APP_URL"
