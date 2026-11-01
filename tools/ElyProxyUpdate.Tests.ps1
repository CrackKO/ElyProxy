BeforeAll {
    . (Join-Path $PSScriptRoot 'ElyProxyUpdateValidation.ps1')

    function New-TestProjectFile {
        param([string]$Content)
        $path = Join-Path $TestDrive 'ElyProxy.csproj'
        Set-Content -LiteralPath $path -Value $Content
        return $path
    }

    function New-CompletePublish {
        param([string]$Directory)
        $files = @(
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
        foreach ($file in $files) {
            $full = Join-Path $Directory $file
            New-Item -ItemType Directory -Path (Split-Path $full -Parent) -Force | Out-Null
            Set-Content -LiteralPath $full -Value 'x'
        }

        $ruleSets = Join-Path $Directory 'Assets\RoscomVPN'
        1..22 | ForEach-Object { Set-Content -LiteralPath (Join-Path $ruleSets "rule-$_.srs") -Value 'x' }
    }
}

Describe 'Get-ElyProxyProjectVersion' {
    It 'reads the Version element from the csproj' {
        $project = New-TestProjectFile -Content '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><Version>1.3.8</Version></PropertyGroup></Project>'

        Get-ElyProxyProjectVersion -ProjectFile $project | Should -Be '1.3.8'
    }

    It 'throws when the project file is missing' {
        { Get-ElyProxyProjectVersion -ProjectFile (Join-Path $TestDrive 'missing.csproj') } |
            Should -Throw '*was not found*'
    }

    It 'throws when the Version element is missing' {
        $project = New-TestProjectFile -Content '<Project><PropertyGroup></PropertyGroup></Project>'

        { Get-ElyProxyProjectVersion -ProjectFile $project } | Should -Throw '*does not contain*'
    }
}

Describe 'Assert-ElyProxyVersionMatches' {
    It 'passes when the project version matches the release version' {
        $project = New-TestProjectFile -Content '<Project><PropertyGroup><Version>1.3.9</Version></PropertyGroup></Project>'

        { Assert-ElyProxyVersionMatches -ProjectFile $project -Version '1.3.9' } | Should -Not -Throw
    }

    It 'throws when the versions differ' {
        $project = New-TestProjectFile -Content '<Project><PropertyGroup><Version>1.3.8</Version></PropertyGroup></Project>'

        { Assert-ElyProxyVersionMatches -ProjectFile $project -Version '1.3.9' } | Should -Throw '*must match*'
    }
}

Describe 'Assert-ElyProxyReleaseNotBuilt' {
    It 'passes when no artifacts exist yet' {
        $release = Join-Path $TestDrive 'release-build\ElyProxy-1.3.9-win-x64'

        {
            Assert-ElyProxyReleaseNotBuilt -PublishDirectory $release -ZipPath "$release.zip" -Version '1.3.9'
        } | Should -Not -Throw
    }

    It 'throws when the publish directory already exists' {
        $release = Join-Path $TestDrive 'release-build\ElyProxy-1.3.9-win-x64'
        New-Item -ItemType Directory -Path $release -Force | Out-Null

        {
            Assert-ElyProxyReleaseNotBuilt -PublishDirectory $release -ZipPath "$release.zip" -Version '1.3.9'
        } | Should -Throw '*already exist*'
    }

    It 'throws when the zip already exists' {
        $release = Join-Path $TestDrive 'release-build\ElyProxy-1.3.9-win-x64'
        New-Item -ItemType Directory -Path (Split-Path $release -Parent) -Force | Out-Null
        Set-Content -LiteralPath "$release.zip" -Value 'zip'

        {
            Assert-ElyProxyReleaseNotBuilt -PublishDirectory $release -ZipPath "$release.zip" -Version '1.3.9'
        } | Should -Throw '*already exist*'
    }
}

Describe 'Assert-ElyProxyPublishComplete' {
    It 'accepts a complete release with all 22 rule sets' {
        $publish = Join-Path $TestDrive 'publish'
        New-CompletePublish -Directory $publish

        { Assert-ElyProxyPublishComplete -PublishDirectory $publish } | Should -Not -Throw
    }

    It 'throws and lists the missing files' {
        $publish = Join-Path $TestDrive 'publish'
        New-CompletePublish -Directory $publish
        Remove-Item -LiteralPath (Join-Path $publish 'sing-box\sing-box.exe')

        { Assert-ElyProxyPublishComplete -PublishDirectory $publish } | Should -Throw '*sing-box.exe*'
    }

    It 'throws when the rule-set count is not 22' {
        $publish = Join-Path $TestDrive 'publish'
        New-CompletePublish -Directory $publish
        Remove-Item -LiteralPath (Join-Path $publish 'Assets\RoscomVPN\rule-1.srs')

        { Assert-ElyProxyPublishComplete -PublishDirectory $publish } | Should -Throw '*22 RoscomVPN rule sets*'
    }
}
