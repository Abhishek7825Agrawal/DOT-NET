@echo off
setlocal
cd /d "%~dp0"
set "SHELFWISE_URL=http://localhost:5180"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo Shelfwise needs the .NET 10 SDK. Install it, then run this file again.
    exit /b 1
)

rem Reuse an existing instance rather than failing to bind the same port twice.
powershell -NoProfile -Command "try { Invoke-WebRequest -Uri '%SHELFWISE_URL%/' -UseBasicParsing -TimeoutSec 2 | Out-Null; exit 0 } catch { if ($_.Exception.Response) { exit 0 }; exit 1 }" >nul 2>nul
if not errorlevel 1 (
    echo Shelfwise is already running at %SHELFWISE_URL%
    exit /b 0
)

rem Honor a SQL Server connection supplied securely through the environment.
if defined ConnectionStrings__DefaultConnection (
    set "DatabaseProvider=SqlServer"
) else (
    if /I "%DatabaseProvider%"=="SqlServer" (
        echo DatabaseProvider is SqlServer but ConnectionStrings__DefaultConnection is missing.
        exit /b 1
    ) else (
        set "DatabaseProvider=Sqlite"
        set "ConnectionStrings__DefaultConnection=Data Source=library.db"
    )
)
set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=%SHELFWISE_URL%"

echo Starting Shelfwise with the local SQLite database.
echo Open %SHELFWISE_URL% after the server starts.
dotnet run --no-launch-profile
if errorlevel 1 (
    echo Shelfwise could not start. Check the error above.
    pause
)
endlocal
