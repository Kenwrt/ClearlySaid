$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentPath = Join-Path $repositoryRoot ".deployment-secrets\local-staging.env"
$baseComposePath = Join-Path $repositoryRoot "compose.staging.yml"
$testComposePath = Join-Path $repositoryRoot "compose.failover-test.yml"
$projectName = "clearlysaid-failover-test"
$testUrl = "http://127.0.0.1:5203"

if (-not (Test-Path -LiteralPath $environmentPath)) {
    throw "Run scripts/Initialize-LocalStaging.ps1 before testing failover."
}

$docker = Get-Command docker -ErrorAction SilentlyContinue
if (-not $docker) {
    $candidate = Join-Path $env:LOCALAPPDATA "Programs\DockerDesktop\resources\bin\docker.exe"
    if (-not (Test-Path -LiteralPath $candidate)) {
        throw "Docker Desktop could not be found."
    }
    $docker = Get-Item -LiteralPath $candidate
}

$tokenLine = Get-Content -LiteralPath $environmentPath |
    Where-Object { $_ -like "STAGING_INTERNAL_API_TOKEN=*" } | Select-Object -First 1
if (-not $tokenLine) {
    throw "The staging internal API token is missing."
}
$internalToken = ($tokenLine -split "=", 2)[1]
$composeArguments = @(
    "compose", "--project-name", $projectName,
    "--project-directory", $repositoryRoot,
    "--env-file", $environmentPath,
    "--file", $baseComposePath,
    "--file", $testComposePath
)

try {
    & $docker @composeArguments up --detach --no-deps --no-build api
    if ($LASTEXITCODE -ne 0) { throw "The failover test API could not be started." }

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 500
        try {
            if ((Invoke-WebRequest -UseBasicParsing "$testUrl/health" -TimeoutSec 2).StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {}
    }
    if (-not $ready) { throw "The failover test API did not become ready." }

    $durations = @()
    foreach ($attempt in 1..2) {
        $body = @{
            message = "local failover test"
            requestId = [Guid]::NewGuid()
            userId = [Guid]::NewGuid()
        } | ConvertTo-Json
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$testUrl/api/messages/refine" `
            -Headers @{ "X-ClearlySaid-Service-Token" = $internalToken } `
            -ContentType "application/json" -Body $body -TimeoutSec 10 -SkipHttpErrorCheck
        $stopwatch.Stop()
        if ($response.StatusCode -ne 503) {
            throw "The failover path returned HTTP $($response.StatusCode) instead of 503."
        }
        $durations += $stopwatch.ElapsedMilliseconds
    }

    $apiLogs = & $docker @composeArguments logs api 2>&1 | Out-String
    if ($apiLogs -notmatch "using OpenAI fallback" -or
        $apiLogs -notmatch "availability circuit is open") {
        throw "The API logs did not confirm both fallback selection and an open circuit."
    }

    if ($durations[1] -ge 1000) {
        throw "The second request was not rejected promptly by the open circuit."
    }

    Write-Host "Local failover passed. The preflight entered the OpenAI path, and the open circuit rejected the second Ollama attempt in $($durations[1]) ms."
}
finally {
    & $docker @composeArguments down --remove-orphans | Out-Null
}
