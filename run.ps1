# Shelfwise one-click PowerShell runner.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

try {
    $sdkVersion = (& dotnet --version).Trim()
} catch {
    Write-Error ".NET 10 SDK is required. Install it and run this script again."
    exit 1
}
if (-not $sdkVersion.StartsWith("10.")) {
    Write-Error "This project requires the .NET 10 SDK. Selected: $sdkVersion"
    exit 1
}

if (-not [string]::IsNullOrWhiteSpace($env:ConnectionStrings__DefaultConnection)) {
    $env:DatabaseProvider = "SqlServer"
    Write-Host "Using the explicitly configured SQL Server connection." -ForegroundColor Green
} elseif ($env:DatabaseProvider -eq "SqlServer") {
    Write-Error "DatabaseProvider is SqlServer but ConnectionStrings__DefaultConnection is missing."
    exit 1
} else {
    $env:DatabaseProvider = "Sqlite"
    $env:ConnectionStrings__DefaultConnection = "Data Source=$PSScriptRoot/library.db"
    Write-Host "Using local SQLite database." -ForegroundColor Yellow
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5000"
Write-Host "Starting Shelfwise at http://localhost:5000. Press Ctrl+C to stop." -ForegroundColor Cyan

& dotnet run --no-launch-profile --project (Join-Path $PSScriptRoot "LibraryManagementSystem.csproj") --urls "http://localhost:5000"
exit $LASTEXITCODE
