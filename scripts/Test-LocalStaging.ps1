$repositoryRoot = Split-Path -Parent $PSScriptRoot
$environmentPath = Join-Path $repositoryRoot ".deployment-secrets\local-staging.env"

if (-not (Test-Path -LiteralPath $environmentPath)) {
    throw "Run scripts/Initialize-LocalStaging.ps1 before testing local staging."
}

$portLine = Get-Content -LiteralPath $environmentPath |
    Where-Object { $_ -like "STAGING_WEB_PORT=*" } | Select-Object -First 1
$port = if ($portLine) { ($portLine -split "=", 2)[1] } else { "5202" }
$baseUrl = "http://localhost:$port"

$health = Invoke-WebRequest -UseBasicParsing "$baseUrl/health"
if ($health.StatusCode -ne 200) { throw "Local staging health check failed." }

foreach ($test in @(
    @{ Method = "Get"; Uri = "$baseUrl/downloads/ClearlySaid-Android-Test.apk" },
    @{ Method = "Post"; Uri = "$baseUrl/api/messages/refine" }
)) {
    try {
        Invoke-WebRequest -UseBasicParsing -Method $test.Method -Uri $test.Uri `
            -ContentType "application/json" -Body $(if ($test.Method -eq "Post") { "{}" } else { $null }) `
            -ErrorAction Stop | Out-Null
        throw "Protected route unexpectedly allowed anonymous access: $($test.Uri)"
    }
    catch {
        if ($_.Exception.Response.StatusCode -ne 401) { throw }
    }
}

$testEmail = "staging-registration-$([Guid]::NewGuid().ToString('N'))@example.test"
$testPassword = "LocalTest$([Guid]::NewGuid().ToString('N'))a"
$registration = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/account/register" `
    -ContentType "application/json" `
    -Body (@{ email = $testEmail; password = $testPassword } | ConvertTo-Json)
if ($registration.message -ne "Your local staging account is ready. You can sign in.") {
    throw "Local staging registration did not auto-verify the test account."
}

$login = Invoke-RestMethod -Method Post -Uri "$baseUrl/api/account/login" `
    -ContentType "application/json" `
    -Body (@{ email = $testEmail; password = $testPassword } | ConvertTo-Json)
if ([string]::IsNullOrWhiteSpace($login.accessToken)) {
    throw "Local staging login did not return an access token."
}

try {
    $account = Invoke-RestMethod -Method Get -Uri "$baseUrl/api/account/me" `
        -Headers @{ Authorization = "Bearer $($login.accessToken)" }
    if ($account.email -ne $testEmail) {
        throw "Local staging returned the wrong test account."
    }
}
finally {
    Invoke-RestMethod -Method Delete -Uri "$baseUrl/api/account" `
        -Headers @{ Authorization = "Bearer $($login.accessToken)" } | Out-Null
}

Write-Host "Local staging passed health, protected-route, registration, and login checks at $baseUrl."
