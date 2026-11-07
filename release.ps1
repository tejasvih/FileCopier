<#
.SYNOPSIS
    Releases the next version: tags main and pushes the tag. GitHub Actions then runs the
    tests, builds the self-contained Windows zip and publishes the GitHub Release.

.EXAMPLE
    ./release.ps1                  # v1.0.0 -> v1.0.1
.EXAMPLE
    ./release.ps1 -Bump minor      # v1.0.1 -> v1.1.0
.EXAMPLE
    ./release.ps1 -Version 2.0.0   # a specific version
.EXAMPLE
    ./release.ps1 -WhatIf          # show the next version without releasing
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [ValidateSet('patch', 'minor', 'major')]
    [string]$Bump = 'patch',

    [string]$Version
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

function Invoke-Git {
    $output = git @args
    if ($LASTEXITCODE -ne 0) { throw "'git $args' failed." }
    $output
}

# Unmet requirements and failures end the script with a plain message and exit code 1.
function Stop-Release([string]$Message, [ConsoleColor]$Color = 'Yellow') {
    Write-Host $Message -ForegroundColor $Color
    exit 1
}

try {
    # Release exactly what is on GitHub's main branch.
    $branch = Invoke-Git branch --show-current
    if ($branch -ne 'main') {
        Stop-Release "Releases are made from main, but you are on '$branch'. Run 'git switch main', then rerun."
    }

    $changes = Invoke-Git status --short
    if ($changes) {
        Stop-Release ("You have changes that are not committed. Commit or stash them, push, then rerun:`n" + ($changes -join "`n"))
    }

    Invoke-Git fetch --quiet --tags origin | Out-Null
    $ahead, $behind = (Invoke-Git rev-list --left-right --count 'HEAD...origin/main') -split '\s+' | ForEach-Object { [int]$_ }
    if ($ahead -and $behind) {
        Stop-Release "Your main and GitHub's main have both changed. Run 'git pull', then 'git push', then rerun."
    }
    if ($ahead) { Stop-Release "You have $ahead commit(s) that are not on GitHub yet. Run 'git push', then rerun." }
    if ($behind) { Stop-Release "GitHub has $behind commit(s) that you do not have yet. Run 'git pull', then rerun." }

    $released = Invoke-Git tag --points-at HEAD --list 'v*'
    if ($released) {
        Stop-Release "This commit is already released as $($released -join ', '). Commit a change before releasing again."
    }

    $latest = Invoke-Git tag --list 'v*' |
        Where-Object { $_ -match '^v\d+\.\d+\.\d+$' } |
        ForEach-Object { [version]$_.Substring(1) } |
        Sort-Object |
        Select-Object -Last 1

    if ($Version) {
        $Version = $Version.TrimStart('v')
        if ($Version -notmatch '^\d+\.\d+\.\d+$') {
            Stop-Release "-Version must have three numbers, like 1.2.3 (got '$Version')."
        }
        $next = [version]$Version
    }
    elseif (-not $latest) { $next = [version]'1.0.0' }
    else {
        $next = switch ($Bump) {
            'major' { [version]::new($latest.Major + 1, 0, 0) }
            'minor' { [version]::new($latest.Major, $latest.Minor + 1, 0) }
            'patch' { [version]::new($latest.Major, $latest.Minor, $latest.Build + 1) }
        }
    }
    if ($latest -and $next -le $latest) {
        Stop-Release "Version $next is not higher than the latest release v$latest. Choose a higher version."
    }
    $tag = "v$next"
    $previous = if ($latest) { "v$latest" } else { 'none' }

    if (-not $PSCmdlet.ShouldProcess("$tag (latest release: $previous)", 'Tag and push release')) {
        if (-not $WhatIfPreference) { Write-Host 'Release canceled. Nothing was changed.' }
        exit 0
    }

    Write-Host "Running tests before releasing $tag..."
    dotnet test PhotoCopier.sln --configuration Release --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { Stop-Release 'Tests failed. Nothing was released.' Red }

    Invoke-Git tag $tag | Out-Null
    try {
        Invoke-Git push --quiet origin $tag | Out-Null
    }
    catch {
        git tag -d $tag | Out-Null
        Stop-Release "Pushing $tag to GitHub failed (see the git message above). Nothing was released; rerun to try again." Red
    }

    $repository = (Invoke-Git remote get-url origin) -replace '\.git$', '' -replace '^git@github\.com:', 'https://github.com/'
    Write-Host "Released $tag. GitHub Actions builds it in about 5 minutes:" -ForegroundColor Green
    Write-Host "  Progress: $repository/actions"
    Write-Host "  Release:  $repository/releases/tag/$tag"
}
catch {
    Stop-Release "Release stopped: $($_.Exception.Message)" Red
}
