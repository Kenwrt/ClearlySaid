param([switch]$Force)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$secretDirectory = Join-Path $repositoryRoot ".deployment-secrets"
$environmentPath = Join-Path $secretDirectory "local-staging.env"

if ((Test-Path -LiteralPath $environmentPath) -and -not $Force) {
    throw "Local staging credentials already exist. Use -Force only when you intend to replace them."
}

function New-HexSecret([int]$byteCount) {
    $bytes = [byte[]]::new($byteCount)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    [Convert]::ToHexString($bytes).ToLowerInvariant()
}

New-Item -ItemType Directory -Path $secretDirectory -Force | Out-Null
$databasePassword = New-HexSecret 24
$internalApiToken = New-HexSecret 32

@"
STAGING_DATABASE_PASSWORD=$databasePassword
STAGING_INTERNAL_API_TOKEN=$internalApiToken
STAGING_WEB_PORT=5202
STAGING_OLLAMA_BASE_URL=http://host.docker.internal:11434/
STAGING_OLLAMA_MODEL=qwen3-vl:4b-instruct
"@ | Set-Content -LiteralPath $environmentPath -Encoding utf8NoBOM

Write-Host "Created isolated local staging configuration at $environmentPath."
Write-Host "Credentials were not displayed, and .deployment-secrets is ignored by Git."
