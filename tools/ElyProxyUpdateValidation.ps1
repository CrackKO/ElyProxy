# Validation helpers for New-ElyProxyUpdate.ps1, split out so they can be
# covered by Pester tests (tools\ElyProxyUpdate.Tests.ps1) without publishing.

function Get-ElyProxyProjectVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ProjectFile
    )

    if (-not (Test-Path -LiteralPath $ProjectFile)) {
        throw "ElyProxy.csproj was not found: $ProjectFile"
    }

    $version = ([xml](Get-Content -Raw -LiteralPath $ProjectFile)).Project.PropertyGroup.Version |
        Where-Object { $_ } |
        Select-Object -First 1

    if (-not $version) {
        throw "ElyProxy.csproj does not contain a <Version> element: $ProjectFile"
    }

    return [string]$version
}

function Assert-ElyProxyVersionMatches {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$ProjectFile,

        [Parameter(Mandatory)]
        [string]$Version
    )

    $projectVersion = Get-ElyProxyProjectVersion -ProjectFile $ProjectFile
    if ($projectVersion -ne $Version) {
        throw "The Version in ElyProxy.csproj ($projectVersion) must match $Version. Update Version, AssemblyVersion, and FileVersion first."
    }
}

function Assert-ElyProxyReleaseNotBuilt {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PublishDirectory,

        [Parameter(Mandatory)]
        [string]$ZipPath,

        [Parameter(Mandatory)]
        [string]$Version
    )

    if ((Test-Path -LiteralPath $PublishDirectory) -or (Test-Path -LiteralPath $ZipPath)) {
        throw "Release artifacts for $Version already exist. Choose a new version to prevent overwriting a published release."
    }
}

function Assert-ElyProxyPublishComplete {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PublishDirectory
    )

    $requiredFiles = @(
        'ElyProxy.exe',
        'updater\ElyProxy.Updater.exe',
        'helper\ElyProxy.Helper.exe',
        'xray\xray.exe',
        'xray\geoip.dat',
        'xray\geosite.dat',
        'xray\wintun.dll',
        'sing-box\sing-box.exe',
        'tun2proxy\tun2proxy-bin.exe',
        'Assets\RoscomVPN\LICENSE-RoscomVPN-Geosite.txt',
        'Assets\RoscomVPN\ROSComVPN-DEFAULT.json'
    )
    $missingFiles = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $PublishDirectory $_)) })
    if ($missingFiles.Count -gt 0) {
        throw "The release is incomplete. Missing: $($missingFiles -join ', ')"
    }

    $ruleSetDirectory = Join-Path $PublishDirectory 'Assets\RoscomVPN'
    $ruleSetCount = @(Get-ChildItem -LiteralPath $ruleSetDirectory -Filter '*.srs' -File -ErrorAction SilentlyContinue).Count
    if ($ruleSetCount -ne 22) {
        throw "The release must include all 22 RoscomVPN rule sets; found $ruleSetCount."
    }
}
