param(
    [string]$Configuration = "Release",
    [string]$OutputPath
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot "artifacts\Api01"
}

$projectPath = Join-Path $repositoryRoot "src\ClearlySaid.Api\ClearlySaid.Api.csproj"

dotnet publish $projectPath `
    --configuration $Configuration `
    --framework net10.0 `
    --output $OutputPath

if ($LASTEXITCODE -ne 0) {
    throw "ClearlySaid.Api publish failed with exit code $LASTEXITCODE."
}

Write-Host "ClearlySaid.Api published to $OutputPath"
