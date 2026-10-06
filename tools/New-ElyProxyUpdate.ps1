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
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'ElyProxy.csproj'
$publishDirectory = Join-Path $projectRoot "release-build\ElyProxy-$Version-win-x64"
$zipPath = Join-Path $projectRoot "release-build\ElyProxy-$Version-win-x64.zip"
$manifestPath = Join-Path $projectRoot 'deployment\elyproxy-update\update.json'

if (-not (Test-Path -LiteralPath $projectFile)) {
    throw "ElyProxy.csproj was not found: $projectFile"
}

$projectVersion = ([xml](Get-Content -Raw -LiteralPath $projectFile)).Project.PropertyGroup.Version |
    Where-Object { $_ } |
    Select-Object -First 1
if ($projectVersion -ne $Version) {
    throw "The Version in ElyProxy.csproj ($projectVersion) must match $Version. Update Version, AssemblyVersion, and FileVersion first."
}

if ((Test-Path -LiteralPath $publishDirectory) -or (Test-Path -LiteralPath $zipPath)) {
    throw "Release artifacts for $Version already exist. Choose a new version to prevent overwriting a published release."
}

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

$requiredFiles = @(
    'ElyProxy.exe',
    'updater\ElyProxy.Updater.exe',
    'xray\xray.exe',
    'xray\geoip.dat',
    'xray\geosite.dat',
    'xray\wintun.dll',
    'sing-box\sing-box.exe',
    'Assets\RoscomVPN\LICENSE-RoscomVPN-Geosite.txt',
    'Assets\RoscomVPN\ROSComVPN-DEFAULT.json'
)
$missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $publishDirectory $_)) })
if ($missingFiles.Count -gt 0) {
    throw "The release is incomplete. Missing: $($missingFiles -join ', ')"
}

$ruleSetDirectory = Join-Path $publishDirectory 'Assets\RoscomVPN'
$ruleSetCount = @(Get-ChildItem -LiteralPath $ruleSetDirectory -Filter '*.srs' -File).Count
if ($ruleSetCount -ne 22) {
    throw "The release must include all 22 RoscomVPN rule sets; found $ruleSetCount."
}

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
