param(
    [string]$Configuration = "Release",
    [string]$OutputPath
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot "artifacts\Web01"
}

$projectPath = Join-Path $repositoryRoot "src\ClearlySaid.Web\ClearlySaid.Web.csproj"

dotnet publish $projectPath `
    --configuration $Configuration `
    --framework net10.0 `
    --output $OutputPath

if ($LASTEXITCODE -ne 0) {
    throw "ClearlySaid.Web publish failed with exit code $LASTEXITCODE."
}

Write-Host "ClearlySaid.Web published to $OutputPath"
