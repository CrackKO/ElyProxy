[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ReleaseNotes,

    [string]$BaseUrl = 'https://elysiumhub.org/updates/elyproxy'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ElyProxyUpdateValidation.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'ElyProxy.csproj'
$publishDirectory = Join-Path $projectRoot "release-build\ElyProxy-$Version-win-x64"
$zipPath = Join-Path $projectRoot "release-build\ElyProxy-$Version-win-x64.zip"
$manifestPath = Join-Path $projectRoot 'deployment\elyproxy-update\update.json'

Assert-ElyProxyVersionMatches -ProjectFile $projectFile -Version $Version
Assert-ElyProxyReleaseNotBuilt -PublishDirectory $publishDirectory -ZipPath $zipPath -Version $Version

Push-Location $projectRoot
try {
    dotnet publish $projectFile -c Release -r win-x64 --self-contained false -o $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Assert-ElyProxyPublishComplete -PublishDirectory $publishDirectory

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
$sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
$packageName = Split-Path -Leaf $zipPath
$manifest = [ordered]@{
    version = $Version
    packageUrl = "$($BaseUrl.TrimEnd('/'))/$packageName"
    sha256 = $sha256
    releaseNotes = $ReleaseNotes
    mandatory = $false
} | ConvertTo-Json -Depth 3

[System.IO.File]::WriteAllText($manifestPath, $manifest + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

Write-Host "Created package: $zipPath"
Write-Host "SHA-256: $sha256"
Write-Host "Updated manifest: $manifestPath"
Write-Host 'Upload the ZIP first, then upload update.json last to /var/www/elysium/public/updates/elyproxy/.'
