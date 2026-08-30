# SteamInvValue installer.
#
# This file is deliberately ASCII-only. A .ps1 with non-ASCII text needs a UTF-8 BOM to run
# correctly on Windows PowerShell 5.1, and that same BOM breaks "irm ... | iex". English
# messages keep both ways of running it working.
#
#   irm https://raw.githubusercontent.com/XYphrodite/steam-inventory-value/main/install.ps1 | iex
#
# With options (iex cannot take parameters, so build a scriptblock):
#
#   & ([scriptblock]::Create((irm <url>))) -Path 'C:\Tools\steaminv' -Components cli -Quiet
#
# Or via environment variables: STEAMINV_INSTALL_DIR, STEAMINV_VERSION, STEAMINV_COMPONENTS,
# GITHUB_TOKEN.
#
# Uninstall:  & ([scriptblock]::Create((irm <url>))) -Uninstall [-Purge]

[CmdletBinding()]
param(
    # Where to install. Default: %LOCALAPPDATA%\Programs\SteamInvValue
    [string]$Path,
    # cli, web or both (default). Validated by hand below: a ValidateSet attribute would
    # blow up under "irm | iex", which applies param attributes to existing session variables.
    [string]$Components,
    # Release tag, e.g. v0.1.0. Default: latest.
    [string]$Version,
    # GitHub token; required while the repository is private. Falls back to GITHUB_TOKEN,
    # then to "gh auth token".
    [string]$Token,
    # Do not touch PATH.
    [switch]$NoPath,
    # Ask nothing, take the defaults.
    [switch]$Quiet,
    # Remove the installed files and the PATH entry.
    [switch]$Uninstall,
    # With -Uninstall: also delete settings, reports and history.
    [switch]$Purge
)

$ErrorActionPreference = 'Stop'
$repo = 'XYphrodite/steam-inventory-value'
$dataDir = Join-Path $env:LOCALAPPDATA 'SteamInvValue'
$defaultDir = Join-Path $env:LOCALAPPDATA 'Programs\SteamInvValue'

function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color }

function Ask([string]$question, [string]$default) {
    if ($Quiet) { return $default }
    $answer = Read-Host "$question [$default]"
    if ([string]::IsNullOrWhiteSpace($answer)) { $default } else { $answer.Trim() }
}

# A token is needed while the repository is private: both the API and the assets are closed.
function Resolve-Token {
    if ($Token) { return $Token }
    if ($env:GITHUB_TOKEN) { return $env:GITHUB_TOKEN }
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        try {
            $t = (gh auth token 2>$null | Select-Object -First 1)
            if ($t) { return $t.Trim() }
        } catch { }
    }
    return $null
}

function Get-AuthHeaders([string]$tok, [string]$accept) {
    $h = @{ 'User-Agent' = 'steaminv-installer'; 'Accept' = $accept }
    if ($tok) { $h['Authorization'] = "Bearer $tok" }
    return $h
}

function Stop-Running([string]$dir) {
    Get-Process -Name 'steaminv', 'SteamInvValue.Web' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($dir, 'OrdinalIgnoreCase') } |
        ForEach-Object {
            Say "  stopping running $($_.ProcessName)"
            $_.Kill()
            $null = $_.WaitForExit(5000)
        }
}

function Remove-FromPath([string]$dir) {
    $current = [Environment]::GetEnvironmentVariable('PATH', 'User')
    if (-not $current) { return $false }
    $parts = @($current -split ';' | Where-Object { $_ })
    $kept = @($parts | Where-Object { $_.TrimEnd('\') -ne $dir.TrimEnd('\') })
    if ($kept.Count -eq $parts.Count) { return $false }
    [Environment]::SetEnvironmentVariable('PATH', ($kept -join ';'), 'User')
    return $true
}

# ---- uninstall ---------------------------------------------------------------------------

if ($Uninstall) {
    $dir = if ($Path) { $Path } elseif ($env:STEAMINV_INSTALL_DIR) { $env:STEAMINV_INSTALL_DIR } else { $defaultDir }
    Say "Removing SteamInvValue from $dir" 'Cyan'

    Stop-Running $dir

    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force; Say '  files removed' }
    else { Say '  folder not found, skipping' 'DarkGray' }

    if (Remove-FromPath $dir) { Say '  PATH entry removed' }

    if ($Purge -and (Test-Path $dataDir)) {
        Remove-Item $dataDir -Recurse -Force
        Say '  settings, reports and history removed'
    } elseif (Test-Path $dataDir) {
        Say "  settings and history kept in $dataDir (use -Uninstall -Purge to drop them)" 'DarkGray'
    }

    Say 'Done.' 'Green'
    return
}

# ---- install -----------------------------------------------------------------------------

Say ''
Say 'SteamInvValue - Steam inventory valuation' 'Cyan'
Say ''

$tok = Resolve-Token
$apiHeaders = Get-AuthHeaders $tok 'application/vnd.github+json'

$tag = if ($Version) { $Version } elseif ($env:STEAMINV_VERSION) { $env:STEAMINV_VERSION } else { $null }
$apiUrl = if ($tag) { "https://api.github.com/repos/$repo/releases/tags/$tag" }
          else      { "https://api.github.com/repos/$repo/releases/latest" }

try {
    $release = Invoke-RestMethod -Uri $apiUrl -Headers $apiHeaders
} catch {
    Say 'Could not fetch the release from GitHub.' 'Red'
    if (-not $tok) {
        Say 'The repository is private, so a token is required:' 'Yellow'
        Say '  gh auth login                   # the token is then picked up automatically' 'Yellow'
        Say '  $env:GITHUB_TOKEN = "<token>"   # or set it by hand' 'Yellow'
    }
    throw
}

$tag = $release.tag_name
Say "Version: $tag"

$dir = if ($Path) { $Path }
       elseif ($env:STEAMINV_INSTALL_DIR) { $env:STEAMINV_INSTALL_DIR }
       else { Ask 'Install to' $defaultDir }
$dir = [Environment]::ExpandEnvironmentVariables($dir)

$what = if ($Components) { $Components }
        elseif ($env:STEAMINV_COMPONENTS) { $env:STEAMINV_COMPONENTS }
        else { Ask 'What to install: cli / web / both' 'both' }
if ($what -notin 'cli', 'web', 'both') { throw "Unknown component set '$what'. Expected cli, web or both." }

$wanted = @()
if ($what -in 'cli', 'both') { $wanted += @{ Asset = 'steaminv-cli-win-x64.zip'; Exe = 'steaminv.exe' } }
if ($what -in 'web', 'both') { $wanted += @{ Asset = 'steaminv-web-win-x64.zip'; Exe = 'SteamInvValue.Web.exe' } }

New-Item -ItemType Directory -Force -Path $dir | Out-Null
Say "Installing into $dir"

# A running copy holds the file open and breaks extraction.
Stop-Running $dir

$temp = Join-Path ([IO.Path]::GetTempPath()) ('steaminv-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    foreach ($item in $wanted) {
        $asset = $release.assets | Where-Object { $_.name -eq $item.Asset } | Select-Object -First 1
        if (-not $asset) { throw "Release $tag has no asset named $($item.Asset)." }

        $zip = Join-Path $temp $item.Asset
        $mb = [math]::Round($asset.size / 1MB, 1)
        Say "  downloading $($item.Asset) ($mb MB)"

        # Assets of a private repository are only served from the API url with this Accept.
        $progress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        try {
            Invoke-WebRequest -Uri $asset.url -Headers (Get-AuthHeaders $tok 'application/octet-stream') `
                -OutFile $zip -UseBasicParsing
        } finally {
            $ProgressPreference = $progress
        }

        Expand-Archive -Path $zip -DestinationPath $dir -Force
        Say "  installed $($item.Exe)"
    }
} finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $NoPath) {
    $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
    $already = $userPath -and (($userPath -split ';') | Where-Object { $_.TrimEnd('\') -eq $dir.TrimEnd('\') })
    if ($already) {
        Say 'PATH: already there'
    } else {
        $joined = if ([string]::IsNullOrWhiteSpace($userPath)) { $dir } else { $userPath.TrimEnd(';') + ';' + $dir }
        [Environment]::SetEnvironmentVariable('PATH', $joined, 'User')
        $env:PATH = $env:PATH.TrimEnd(';') + ';' + $dir
        Say 'PATH: added (new terminal windows pick it up on their own)'
    }
}

Say ''
Say 'Done.' 'Green'
if ($what -in 'cli', 'both') {
    Say '  steaminv add https://steamcommunity.com/id/nickname   add an inventory'
    Say '  steaminv                                              value everything in the config'
    Say '  steaminv --help                                       every other command'
}
if ($what -in 'web', 'both') {
    Say "  $(Join-Path $dir 'SteamInvValue.Web.exe')"
    Say '                                                        local site on http://localhost:5188'
}
Say ''
Say "Settings and history: $dataDir" 'DarkGray'
Say 'Uninstall: run this script with -Uninstall (add -Purge to drop settings too).' 'DarkGray'
Say ''
