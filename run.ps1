# Shelfwise One-Click PowerShell Runner
$ErrorActionPreference = "Continue"

$targetDir = $PSScriptRoot
if (Test-Path (Join-Path $PSScriptRoot "DOT-NET-main")) {
    $targetDir = Join-Path $PSScriptRoot "DOT-NET-main"
}
Set-Location $targetDir

Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host "   Shelfwise Library Management System" -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Cyan
Write-Host ""

# Check Docker & SQL Server container
Write-Host "[1/2] Checking Database..." -ForegroundColor Cyan
$sqlReady = $false
try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $tcp.Connect("127.0.0.1", 1433)
    $tcp.Close()
    $sqlReady = $true
} catch {
    # If port 1433 is closed, try starting the container
    Write-Host "Attempting to start 'shelfwise-sqlserver' container..." -ForegroundColor Gray
    docker start shelfwise-sqlserver 2>$null | Out-Null
    Start-Sleep -Seconds 3
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient
        $tcp.Connect("127.0.0.1", 1433)
        $tcp.Close()
        $sqlReady = $true
    } catch {}
}

if ($sqlReady) {
    Write-Host "[OK] Connected to SQL Server 2022 (localhost:1433)" -ForegroundColor Green
    $env:DatabaseProvider = "SqlServer"
    $env:ConnectionStrings__DefaultConnection = "Server=localhost,1433;Database=LibraryManagementSystem;User Id=sa;Password=Abhi7825;TrustServerCertificate=True;"
} else {
    Write-Host "[WARN] SQL Server 1433 not reachable. Using local SQLite (library.db)" -ForegroundColor Yellow
    $env:DatabaseProvider = "Sqlite"
    $env:ConnectionStrings__DefaultConnection = "Data Source=library.db"
}

$env:ASPNETCORE_ENVIRONMENT = "Development"

Write-Host ""
Write-Host "[2/2] Starting Shelfwise Web Server at http://localhost:5000 ..." -ForegroundColor Cyan
Write-Host "=======================================================" -ForegroundColor Yellow
Write-Host "  URL:          http://localhost:5000" -ForegroundColor Yellow
Write-Host "  Admin Login:  admin@shelfwise.local  / Admin@12345!" -ForegroundColor Yellow
Write-Host "  Member Login: member@shelfwise.local / Member@12345!" -ForegroundColor Yellow
Write-Host "=======================================================" -ForegroundColor Yellow
Write-Host "Press Ctrl+C in this window to stop the server.`n" -ForegroundColor Gray

dotnet run --urls "http://localhost:5000"
