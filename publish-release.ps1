[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Version,
    [string]$Token,
    [switch]$FirstRelease,
    [switch]$AllowDirtyWorkingTree
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$projectPath = Join-Path $projectRoot 'verba-windows.csproj'
$testProjectPath = Join-Path $projectRoot 'Tests\verba-windows.Tests.csproj'
$iconPath = Join-Path $projectRoot 'Assets\AppIcon.ico'
$repositoryUrl = 'https://github.com/phungbuuquang/verba-windows'
$channel = 'win-x64-stable'
$runtime = 'win-x64'
$dotEnvPath = Join-Path $projectRoot '.env'

function Read-DotEnvValue {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    foreach ($line in Get-Content -LiteralPath $Path -Encoding utf8) {
        $text = $line.Trim()
        if ($text.Length -eq 0 -or $text.StartsWith('#')) { continue }
        if ($text.StartsWith('export ', [StringComparison]::OrdinalIgnoreCase)) {
            $text = $text.Substring(7).Trim()
        }
        $separator = $text.IndexOf('=')
        if ($separator -lt 1) { continue }
        $key = $text.Substring(0, $separator).Trim()
        if (-not $key.Equals($Name, [StringComparison]::OrdinalIgnoreCase)) { continue }

        $value = $text.Substring($separator + 1).Trim()
        if ($value.Length -ge 2 -and
            (($value.StartsWith('"') -and $value.EndsWith('"')) -or
             ($value.StartsWith("'") -and $value.EndsWith("'")))) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        return $value
    }
    return $null
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = $env:VERBA_GITHUB_TOKEN
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = Read-DotEnvValue -Path $dotEnvPath -Name 'VERBA_GITHUB_TOKEN'
}
if (-not $WhatIfPreference -and [string]::IsNullOrWhiteSpace($Token)) {
    throw 'VERBA_GITHUB_TOKEN is missing from both the environment and .env.'
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)] [string]$Description,
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    Write-Host "`n==> $Description" -ForegroundColor Cyan
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-PathInsideProject {
    param([Parameter(Mandatory)] [string]$Path)

    $fullPath = [IO.Path]::GetFullPath($Path)
    $rootWithSeparator = [IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\'
    if (-not $fullPath.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use a release path outside the repository: $fullPath"
    }
    return $fullPath
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath -Encoding utf8
$projectVersion = $projectXml.Project.PropertyGroup |
    ForEach-Object { $_.Version } |
    Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
    Select-Object -First 1
$projectVersion = [string]$projectVersion

if ([string]::IsNullOrWhiteSpace($projectVersion)) {
    throw 'The project does not declare a Version.'
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $projectVersion
}
if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Invalid release version '$Version'. Use a SemVer value such as 1.2.3."
}
if ($Version -ne $projectVersion) {
    throw "Version mismatch: script received '$Version' but verba-windows.csproj contains '$projectVersion'."
}

Push-Location $projectRoot
try {
    if (-not $AllowDirtyWorkingTree) {
        $changes = & git status --porcelain --untracked-files=all
        if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the Git working tree.' }
        if ($changes) {
            throw 'The Git working tree is not clean. Commit/stash changes, or explicitly pass -AllowDirtyWorkingTree.'
        }
    }

    $existingTag = & git tag --list "v$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect Git tags.' }
    if ($existingTag) { throw "Git tag v$Version already exists locally." }

    Invoke-NativeCommand 'Restore test dependencies' 'dotnet' @('restore', $testProjectPath)
    Invoke-NativeCommand 'Restore application dependencies' 'dotnet' @('restore', $projectPath, '-r', $runtime)
    Invoke-NativeCommand 'Restore Velopack CLI' 'dotnet' @('tool', 'restore')
    Invoke-NativeCommand 'Build Release' 'dotnet' @('build', $projectPath, '-c', 'Release', '--no-restore')
    Invoke-NativeCommand 'Run regression tests' 'dotnet' @(
        'run', '--project', $testProjectPath, '-c', 'Release', '--no-restore'
    )

    $releaseRoot = Assert-PathInsideProject (Join-Path $projectRoot "artifacts\release\$Version")
    $publishDirectory = Join-Path $releaseRoot "publish\$runtime"
    $releaseDirectory = Join-Path $releaseRoot 'packages'
    if (Test-Path -LiteralPath $releaseRoot) {
        Remove-Item -LiteralPath $releaseRoot -Recurse -Force -WhatIf:$false -Confirm:$false
    }
    New-Item -ItemType Directory -Path $publishDirectory, $releaseDirectory -Force -WhatIf:$false | Out-Null

    if (-not $FirstRelease) {
        Invoke-NativeCommand 'Download the current release for delta packaging' 'dotnet' @(
            'tool', 'run', 'vpk', '--', 'download', 'github',
            '--repoUrl', $repositoryUrl,
            "--channel=$channel",
            '--outputDir', $releaseDirectory
        )
    }

    Invoke-NativeCommand 'Publish the self-contained Windows application' 'dotnet' @(
        'publish', $projectPath,
        '-c', 'Release',
        '-r', $runtime,
        '--self-contained', 'true',
        "-p:Version=$Version",
        '-p:PublishSingleFile=false',
        '--no-restore',
        '-o', $publishDirectory
    )

    Invoke-NativeCommand 'Create Velopack packages' 'dotnet' @(
        'tool', 'run', 'vpk', '--', 'pack',
        '--packId', 'Verba.Windows',
        '--packVersion', $Version,
        '--packDir', $publishDirectory,
        '--mainExe', 'verba-windows.exe',
        '--icon', $iconPath,
        "--channel=$channel",
        '--outputDir', $releaseDirectory
    )

    $releaseTarget = "$repositoryUrl/releases/tag/v$Version"
    if ($PSCmdlet.ShouldProcess($releaseTarget, 'Upload and publish the GitHub Release')) {
        if ([string]::IsNullOrWhiteSpace($Token)) {
            throw 'VERBA_GITHUB_TOKEN is missing. Set it in the current terminal before publishing.'
        }
        Invoke-NativeCommand 'Upload and publish the GitHub Release' 'dotnet' @(
            'tool', 'run', 'vpk', '--', 'upload', 'github',
            '--repoUrl', $repositoryUrl,
            '--token', $Token,
            "--channel=$channel",
            '--publish',
            '--tag', "v$Version",
            '--releaseName', "Verba $Version",
            '--outputDir', $releaseDirectory
        )
        Write-Host "`nPublished: $releaseTarget" -ForegroundColor Green
    }
    else {
        Write-Host "`nPackages are ready at: $releaseDirectory" -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}
