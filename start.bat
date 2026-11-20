@echo off
setlocal
title Shelfwise Library Management System
cd /d "%~dp0"

echo =======================================================
echo   Shelfwise Library Management System - Startup
echo =======================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET 10 SDK is not installed or not in PATH.
    echo Install the .NET 10 SDK and run this file again.
    pause
    exit /b 1
)

for /f "delims=" %%V in ('dotnet --version 2^>nul') do set "DOTNET_SDK_VERSION=%%V"
if not "%DOTNET_SDK_VERSION:~0,3%"=="10." (
    echo [ERROR] This project requires the .NET 10 SDK. Selected: %DOTNET_SDK_VERSION%
    pause
    exit /b 1
)

if defined ConnectionStrings__DefaultConnection (
    set "DatabaseProvider=SqlServer"
    echo [OK] Using explicitly configured SQL Server connection.
) else (
    if /i "%DatabaseProvider%"=="SqlServer" (
        echo [ERROR] DatabaseProvider is SqlServer but ConnectionStrings__DefaultConnection is missing.
        pause
        exit /b 1
    )
    set "DatabaseProvider=Sqlite"
    set "ConnectionStrings__DefaultConnection=Data Source=%CD%\library.db"
    echo [OK] Using local SQLite database.
)

set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=http://localhost:5000"

echo.
echo Starting Shelfwise at http://localhost:5000
echo Press Ctrl+C in this window to stop the server.
echo.
dotnet run --no-launch-profile --project "%~dp0LibraryManagementSystem.csproj" --urls "http://localhost:5000"
if errorlevel 1 (
    echo.
    echo [ERROR] Shelfwise stopped with an error. Review the messages above.
    pause
    exit /b 1
)

endlocal
