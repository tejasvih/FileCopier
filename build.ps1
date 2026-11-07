<#
.SYNOPSIS
    Builds PhotoCopier.exe as one self-contained Windows executable that runs without .NET installed.

.DESCRIPTION
    Runs the tests, then publishes with the same settings as the GitHub release build.
    Settings already in the output folder are kept, so rebuilding never resets the backup folder.
    Files left there by an earlier build of this app are replaced; other files are not touched.

.EXAMPLE
    ./build.ps1                              # tests, then builds into .\publish
.EXAMPLE
    ./build.ps1 -Output D:\Tools\PhotoCopier
.EXAMPLE
    ./build.ps1 -SkipTests
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot 'publish'),
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$Output = [IO.Path]::GetFullPath($Output)

$running = Get-Process -Name PhotoCopier, iPhonePhotoCopier -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($Output, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "Photo Copier is running from $Output. Close it, then rebuild." }

if (-not $SkipTests) {
    Write-Host 'Running tests...'
    dotnet test PhotoCopier.sln --configuration Release --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. Nothing was built.' }
}

# Publish into an empty staging folder first, so a failed build leaves the output folder as it was.
$staging = Join-Path ([IO.Path]::GetTempPath()) "PhotoCopier-build-$([guid]::NewGuid().ToString('N'))"
try {
    Write-Host 'Publishing self-contained executable...'
    dotnet publish FileCopier/PhotoCopier.csproj --configuration Release --runtime win-x64 --self-contained `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
        --nologo --verbosity quiet --output $staging
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed. The output folder was not changed.' }

    New-Item -ItemType Directory -Force -Path $Output | Out-Null

    # Earlier builds: this one, the framework-dependent layout, and the app's previous name.
    $previousBuild = 'PhotoCopier.*', 'iPhonePhotoCopier.*', 'MediaDevices.*', 'Microsoft.Extensions.*.dll'
    Get-ChildItem -LiteralPath $Output -File |
        Where-Object { $name = $_.Name; $previousBuild | Where-Object { $name -like $_ } } |
        Remove-Item

    # Keep settings already edited in the output folder.
    if (Test-Path -LiteralPath (Join-Path $Output 'appsettings.json')) {
        Remove-Item -LiteralPath (Join-Path $staging 'appsettings.json')
        Write-Host 'Kept the existing appsettings.json.'
    }
    Copy-Item -Path (Join-Path $staging '*') -Destination $Output -Recurse -Force

    # Personal settings for a local build, unless the output folder already has its own.
    $localSettings = 'FileCopier/appsettings.local.json'
    if ((Test-Path -LiteralPath $localSettings) -and -not (Test-Path -LiteralPath (Join-Path $Output 'appsettings.local.json'))) {
        Copy-Item -LiteralPath $localSettings -Destination $Output
        Write-Host 'Copied FileCopier/appsettings.local.json.'
    }
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

$exe = Get-Item -LiteralPath (Join-Path $Output 'PhotoCopier.exe')
Write-Host ("Built {0} ({1:N0} MB). No .NET installation is needed to run it." -f $exe.FullName, ($exe.Length / 1MB))
