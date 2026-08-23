param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v\d+\.\d+\.\d+$')]
    [string]$ReleaseTag
)

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repositoryRoot
try {
    if ((git branch --show-current) -ne "main") {
        throw "Production release validation requires the main branch."
    }
    if (git status --porcelain) {
        throw "Production release validation requires a clean working tree."
    }
    git fetch origin main --tags
    if ($LASTEXITCODE -ne 0) { throw "Unable to refresh production Git references." }

    $head = git rev-parse HEAD
    $remoteMain = git rev-parse origin/main
    $tagCommit = git rev-list -n 1 $ReleaseTag
    if (-not $tagCommit) { throw "Release tag $ReleaseTag does not exist." }
    if ($head -ne $remoteMain -or $head -ne $tagCommit) {
        throw "HEAD, origin/main, and $ReleaseTag must identify the same commit."
    }

    Write-Host "Production release validation passed for $ReleaseTag at $head."
    Write-Host "A separate explicit approval is still required before Web01 or API01 deployment."
}
finally {
    Pop-Location
}
