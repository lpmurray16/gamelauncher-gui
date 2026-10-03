[CmdletBinding()]
param([string]$IsccPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\GameLauncher\GameLauncher.csproj'
$publish = Join-Path $root 'artifacts\publish\win-x64'
$prerequisites = Join-Path $root 'artifacts\prerequisites'
$bootstrapper = Join-Path $prerequisites 'MicrosoftEdgeWebview2Setup.exe'

if (-not $IsccPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $IsccPath = $compiler.Source }
    else {
        $IsccPath = @(
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Install Inno Setup 6 or pass -IsccPath with the full path to ISCC.exe.'
}
[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = [string]$projectXml.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The project Version must be a numeric major.minor.patch release.' }

Push-Location $root
try {
    # Clean only this generated publish directory to avoid including stale output.
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    & dotnet publish $project '-p:PublishProfile=Windows' -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }
    $app = Join-Path $publish 'GameLauncher.exe'
    if (-not (Test-Path -LiteralPath $app)) { throw 'Publish did not produce GameLauncher.exe.' }
    if ((Get-Item -LiteralPath $app).VersionInfo.FileVersion -ne "$version.0") {
        throw 'The published executable version does not match the release version.'
    }

    New-Item -ItemType Directory -Force $prerequisites | Out-Null
    if (-not (Test-Path -LiteralPath $bootstrapper)) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper -UseBasicParsing
    }
    $signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
        throw 'WebView2 bootstrapper must have a valid Microsoft signature. Remove the cached prerequisite and retry.'
    }

    # Retain upstream license/notice files and package metadata, not a license for this project.
    $assets = Get-Content (Join-Path $root 'src\GameLauncher\obj\project.assets.json') -Raw | ConvertFrom-Json
    $packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
    $packagePaths = @($assets.libraries.PSObject.Properties | ForEach-Object { $_.Value.path })
    foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
        foreach ($dependency in $framework.Value.downloadDependencies) {
            $runtimeVersion = ($dependency.version.Trim('[', ']') -split ',')[0].Trim()
            $packagePaths += $dependency.name.ToLowerInvariant() + '/' + $runtimeVersion
        }
    }
    $notices = Join-Path $publish 'ThirdPartyNotices'
    New-Item -ItemType Directory -Force $notices | Out-Null
    foreach ($relative in ($packagePaths | Sort-Object -Unique)) {
        $package = $null
        foreach ($packageRoot in $packageRoots) {
            $candidate = Join-Path $packageRoot $relative
            if (Test-Path -LiteralPath $candidate) { $package = $candidate; break }
        }
        if (-not $package) { throw "Cannot locate restored package $relative." }
        $destination = Join-Path $notices ($relative -replace '[/\\]', '-')
        New-Item -ItemType Directory -Force $destination | Out-Null
        Get-ChildItem -LiteralPath $package -File | Where-Object {
            $_.Name -match '(?i)license|notice|copying|\.nuspec$'
        } | Copy-Item -Destination $destination
    }

    Copy-Item (Join-Path $root 'installer\ThirdPartyNotices\*') -Destination $notices

    & $IsccPath "/DAppVersion=$version" (Join-Path $root 'installer\GameLauncher.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed ($LASTEXITCODE)." }
    $installer = Join-Path $root "artifacts\installer\GameLauncher-Setup-$version-win-x64.exe"
    if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer output is missing.' }
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath "$installer.sha256" -Encoding ASCII
    Write-Host "Installer: $installer"
    Write-Host "SHA256:    $hash"
    Write-Host 'Unsigned installer built. Installation, upgrades, and uninstall still need a manual walkthrough.'
}
finally { Pop-Location }
