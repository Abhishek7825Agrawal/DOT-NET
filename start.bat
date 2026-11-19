@echo off
setlocal enabledelayedexpansion
title Shelfwise Library Management System
cd /d "%~dp0"
set "SHELFWISE_URL=http://localhost:5000"

echo =======================================================
echo   Shelfwise Library Management System - Startup
echo =======================================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK is not installed or not in PATH!
    echo Please install .NET 10 SDK: https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

echo [1/2] Checking Database status...
set "SQL_READY=0"

rem Check if SQL Server port 1433 is responding
powershell -NoProfile -Command "$t = New-Object Net.Sockets.TcpClient; try { $t.Connect('127.0.0.1', 1433); $t.Close(); exit 0 } catch { exit 1 }" >nul 2>nul
if not errorlevel 1 (
    set "SQL_READY=1"
    goto :DbDetected
)

rem If 1433 is closed, try starting the docker container
where docker >nul 2>nul
if not errorlevel 1 (
    echo Attempting to start 'shelfwise-sqlserver' container...
    docker start shelfwise-sqlserver >nul 2>nul
    timeout /t 3 /nobreak >nul
    powershell -NoProfile -Command "$t = New-Object Net.Sockets.TcpClient; try { $t.Connect('127.0.0.1', 1433); $t.Close(); exit 0 } catch { exit 1 }" >nul 2>nul
    if not errorlevel 1 (
        set "SQL_READY=1"
    )
)

:DbDetected
if "!SQL_READY!"=="1" (
    echo [OK] Using SQL Server 2022 (localhost:1433)
    set "DatabaseProvider=SqlServer"
    set "ConnectionStrings__DefaultConnection=Server=localhost,1433;Database=LibraryManagementSystem;User Id=sa;Password=Abhi7825;TrustServerCertificate=True;"
) else (
    echo [WARN] SQL Server 1433 not reachable. Using local SQLite fallback (library.db)
    set "DatabaseProvider=Sqlite"
    set "ConnectionStrings__DefaultConnection=Data Source=library.db"
)

set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=%SHELFWISE_URL%"
set "DOTNET_URLS=%SHELFWISE_URL%"

echo.
echo [2/2] Starting Shelfwise Web Server...
echo =======================================================
echo   URL:          %SHELFWISE_URL%
echo   Admin Login:  admin@shelfwise.local  / Admin@12345!
echo   Staff Login:  staff@shelfwise.local  / Staff@12345!
echo   Member Login: member@shelfwise.local / Member@12345!
echo =======================================================
echo Server band karne ke liye is window me Ctrl+C press karein.
echo.

dotnet run --urls "%SHELFWISE_URL%"
if errorlevel 1 (
    echo.
    echo =======================================================
    echo [ERROR] Shelfwise stopped with an error!
    echo =======================================================
)
echo.
pause
endlocal
