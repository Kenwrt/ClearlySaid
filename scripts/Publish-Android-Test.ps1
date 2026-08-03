param(
    [string]$Configuration = "Debug"
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\ClearlySaid.App\ClearlySaid.App.csproj"
$downloadDirectory = Join-Path $repositoryRoot "src\ClearlySaid.Web\wwwroot\downloads"
$sourceApk = Join-Path $repositoryRoot "src\ClearlySaid.App\bin\$Configuration\net10.0-android\com.clearlysaid.app-Signed.apk"
$destinationApk = Join-Path $downloadDirectory "ClearlySaid-Android-Test.apk"
$checksumPath = "$destinationApk.sha256"

dotnet build $projectPath `
    --configuration $Configuration `
    --framework net10.0-android `
    -p:AndroidPackageFormats=apk

if ($LASTEXITCODE -ne 0) {
    throw "ClearlySaid Android build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $sourceApk)) {
    throw "The signed APK was not found at $sourceApk."
}

New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
Copy-Item -LiteralPath $sourceApk -Destination $destinationApk -Force

$hash = (Get-FileHash -LiteralPath $destinationApk -Algorithm SHA256).Hash
"$hash  ClearlySaid-Android-Test.apk" | Set-Content -LiteralPath $checksumPath -Encoding ascii

Write-Host "Android test APK published to $destinationApk"
Write-Host "SHA-256: $hash"
