# Makables — local dev launcher.
#
# Starts all four per-audience API hosts (Customer 5001, Maker 5002,
# Admin 5003, Public 5104) in Development, each in its own window, after a
# preflight that the local Postgres (:5432) is reachable. The hosts read
# their local stub secrets from appsettings.Development.json, so no manual
# env-var setup is needed — this mirrors the CI spec-parity boot.
#
# Usage:
#   pwsh scripts/run-dev.ps1              # start all four hosts
#   pwsh scripts/run-dev.ps1 -Build       # dotnet build first, then start
#   pwsh scripts/run-dev.ps1 -Host Customer   # start only one host
#
# Prerequisites (see docs/deployment/local-dev.md):
#   - .NET 10 SDK
#   - Postgres 16 on localhost:5432 (db: makables_dev, user/pass: postgres)
#   - Azurite (blob+queue) — optional; only order attachments / outbox need it.

param(
    [switch]$Build,
    [ValidateSet('Customer', 'Maker', 'Admin', 'Public', 'All')]
    [string]$Host = 'All'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'backend/src'

$hosts = @(
    @{ Name = 'Customer'; Project = 'Makables.Web.Customer'; Port = 5001 }
    @{ Name = 'Maker';    Project = 'Makables.Web.Maker';    Port = 5002 }
    @{ Name = 'Admin';    Project = 'Makables.Web.Admin';    Port = 5003 }
    @{ Name = 'Public';   Project = 'Makables.Web.Public';   Port = 5104 }
)

if ($Host -ne 'All') {
    $hosts = $hosts | Where-Object { $_.Name -eq $Host }
}

# --- Preflight: Postgres reachable? -----------------------------------------
Write-Host 'Preflight: checking Postgres on localhost:5432 ...' -ForegroundColor Cyan
$pg = Test-NetConnection -ComputerName 127.0.0.1 -Port 5432 -InformationLevel Quiet -WarningAction SilentlyContinue
if (-not $pg) {
    Write-Host 'Postgres is NOT reachable on localhost:5432.' -ForegroundColor Red
    Write-Host 'Start it first (docker: `docker start postgres-<id>` or run your local Postgres 16).' -ForegroundColor Yellow
    exit 1
}
Write-Host 'Postgres OK.' -ForegroundColor Green

# --- Optional build ---------------------------------------------------------
if ($Build) {
    Write-Host 'Building solution ...' -ForegroundColor Cyan
    dotnet build (Join-Path $src 'Makables.Api.slnx') -c Debug --nologo
    if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed.' -ForegroundColor Red; exit 1 }
}

# --- Launch each host in its own window -------------------------------------
foreach ($h in $hosts) {
    $proj = Join-Path $src $h.Project
    Write-Host ("Starting {0} on http://localhost:{1} ..." -f $h.Name, $h.Port) -ForegroundColor Cyan
    Start-Process pwsh -ArgumentList @(
        '-NoExit', '-Command',
        "`$env:ASPNETCORE_ENVIRONMENT='Development'; Set-Location '$proj'; dotnet run --launch-profile http"
    )
}

Write-Host ''
Write-Host 'All requested hosts launching. Endpoints:' -ForegroundColor Green
foreach ($h in $hosts) {
    Write-Host ("  {0,-9} http://localhost:{1}   (openapi: /openapi/v1.json)" -f $h.Name, $h.Port)
}
Write-Host ''
Write-Host 'Frontend: cd frontend; npm run dev   (defaults to these ports, no env needed)' -ForegroundColor DarkGray
