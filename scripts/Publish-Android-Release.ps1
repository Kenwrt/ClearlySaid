param(
    [string]$Configuration = "Release",
    [string]$OutputPath,
    [string]$KeystorePath = $env:CLEARLYSAID_ANDROID_KEYSTORE,
    [string]$KeyAlias = $env:CLEARLYSAID_ANDROID_KEY_ALIAS
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repositoryRoot "artifacts\Android-Release"
}

if ([string]::IsNullOrWhiteSpace($KeystorePath) -or -not (Test-Path -LiteralPath $KeystorePath)) {
    throw "Set CLEARLYSAID_ANDROID_KEYSTORE to the protected upload-keystore path."
}

if ([string]::IsNullOrWhiteSpace($KeyAlias)) {
    throw "Set CLEARLYSAID_ANDROID_KEY_ALIAS to the upload-key alias."
}

if ([string]::IsNullOrWhiteSpace($env:CLEARLYSAID_ANDROID_STORE_PASSWORD) -or
    [string]::IsNullOrWhiteSpace($env:CLEARLYSAID_ANDROID_KEY_PASSWORD)) {
    throw "Set the Android store and key password environment variables before publishing."
}

$projectPath = Join-Path $repositoryRoot "src\ClearlySaid.App\ClearlySaid.App.csproj"
dotnet publish $projectPath `
    --configuration $Configuration `
    --framework net10.0-android `
    -p:AndroidPackageFormats=aab `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore="$KeystorePath" `
    -p:AndroidSigningKeyAlias="$KeyAlias" `
    -p:AndroidSigningStorePass="env:CLEARLYSAID_ANDROID_STORE_PASSWORD" `
    -p:AndroidSigningKeyPass="env:CLEARLYSAID_ANDROID_KEY_PASSWORD" `
    -p:PublishDir="$OutputPath\"

if ($LASTEXITCODE -ne 0) {
    throw "ClearlySaid Android App Bundle publish failed with exit code $LASTEXITCODE."
}

Write-Host "Signed ClearlySaid Android App Bundle published to $OutputPath"
