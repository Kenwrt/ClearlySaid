$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentPath = Join-Path $repositoryRoot ".deployment-secrets\local-staging.env"
$composePath = Join-Path $repositoryRoot "compose.staging.yml"

if (-not (Test-Path -LiteralPath $environmentPath)) {
    throw "Run scripts/Initialize-LocalStaging.ps1 before starting local staging."
}

docker compose --project-directory $repositoryRoot --env-file $environmentPath `
    --file $composePath up --detach --build

if ($LASTEXITCODE -ne 0) {
    throw "Local staging startup failed with exit code $LASTEXITCODE."
}

$portLine = Get-Content -LiteralPath $environmentPath |
    Where-Object { $_ -like "STAGING_WEB_PORT=*" } | Select-Object -First 1
$port = if ($portLine) { ($portLine -split "=", 2)[1] } else { "5202" }
Write-Host "ClearlySaid local staging is starting at http://localhost:$port"
