$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentPath = Join-Path $repositoryRoot ".deployment-secrets\local-staging.env"
$composePath = Join-Path $repositoryRoot "compose.staging.yml"

if (-not (Test-Path -LiteralPath $environmentPath)) {
    throw "The local staging environment has not been initialized."
}

docker compose --project-directory $repositoryRoot --env-file $environmentPath `
    --file $composePath down

if ($LASTEXITCODE -ne 0) {
    throw "Local staging shutdown failed with exit code $LASTEXITCODE."
}

Write-Host "ClearlySaid local staging stopped. Staging data was retained."
