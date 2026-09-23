#Requires -Version 5.1
<#
.SYNOPSIS
    Creates or repairs Assets\_External, the junction that mounts the art folder into the
    Unity project.

.DESCRIPTION
    Art is authored outside the repository and is never committed. Unity sees it through a
    directory junction:

        <repo>\Assets\_External  ->  E:\tx2\素材

    A junction needs no administrator rights (a symbolic link would), and it keeps a single
    copy of every file for both the art author and Unity.

    Junctions are machine local and cannot travel with the repository, so a fresh clone - or
    a new machine - needs this script once. It is idempotent:

        missing link      -> created
        correct junction  -> reported, left untouched
        wrong target      -> replaced only when -Repair is passed
        real directory    -> never deleted; the script stops and asks a human to move it
                             aside, because deleting it would throw away hand-copied art

    Unity writes .meta files next to the source assets, which is expected: the art folder
    has to stay writable. The script probes for that and warns when it cannot write.

    Exit codes:  0 = junction is in place,  2 = not set up / set up wrongly.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File E:\tx2\samsara-west\Tools\setup-external-assets.ps1

.EXAMPLE
    # Point at a different art folder and replace an existing link in one go.
    powershell -NoProfile -ExecutionPolicy Bypass -File ...\setup-external-assets.ps1 -SourcePath 'D:\art' -Repair
#>
[CmdletBinding()]
param(
    # The one true copy of the art. Defaults to the folder used on this machine. The two
    # non-ASCII characters are spelled out as char codes on purpose: this file is kept pure
    # ASCII because Windows PowerShell 5.1 decodes a BOM-less .ps1 as GBK and would mangle a
    # literal path (result: "folder not found" on a folder that clearly exists).
    [string] $SourcePath = ('E:\tx2\' + [char] 0x7D20 + [char] 0x6750),

    # Mount point inside the project. Defaults to <repo>\Assets\_External.
    [string] $LinkPath = '',

    # Replace a junction that currently points somewhere else.
    [switch] $Repair
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Line {
    param([string] $Message, [string] $Colour = 'Gray')
    Write-Host "[external] $Message" -ForegroundColor $Colour
}

function Stop-Setup {
    param([string] $Message)
    Write-Line $Message 'Red'
    exit 2
}

function Get-NormalisedPath {
    param([Parameter(Mandatory)][string] $Path)
    return $Path.TrimEnd('\', '/')
}

function Test-IsReparsePoint {
    param([Parameter(Mandatory)] $Item)
    return [bool] ($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
}

function Get-JunctionTargets {
    param([Parameter(Mandatory)] $Item)
    return @($Item.Target) | ForEach-Object { Get-NormalisedPath -Path ([string] $_) }
}

function Remove-Junction {
    <#
        A junction is a reparse point, so Remove-Item -Recurse on it would delete the CONTENT
        of the art folder - exactly what the junction exists to protect. `rmdir` removes the
        link only, hence the call out to cmd.exe. The command is assembled as one string so
        paths with spaces survive the trip through PowerShell's native argument quoting.
    #>
    param([Parameter(Mandatory)][string] $Path)

    & cmd.exe /c ('rmdir "' + $Path + '"') | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Stop-Setup "Failed to remove the existing junction at '$Path' (rmdir exit $LASTEXITCODE)."
    }
}

function New-Junction {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][string] $Target)

    New-Item -ItemType Junction -Path $Path -Target $Target | Out-Null
}

$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path

if (-not $LinkPath) {
    $LinkPath = Join-Path $projectRoot 'Assets\_External'
}
$LinkPath = Get-NormalisedPath -Path $LinkPath

Write-Line "project : $projectRoot"
Write-Line "mount   : $LinkPath"

if (-not (Test-Path -LiteralPath $SourcePath -PathType Container)) {
    Stop-Setup "Art folder not found: '$SourcePath'. Pass -SourcePath <folder> if it lives elsewhere."
}

$source = Get-NormalisedPath -Path (Resolve-Path -LiteralPath $SourcePath).Path
Write-Line "source  : $source"

if ($source.StartsWith(($projectRoot + '\'), [System.StringComparison]::OrdinalIgnoreCase)) {
    Stop-Setup "Art folder '$source' sits inside the repository and would end up in git. Keep it outside '$projectRoot'."
}

$existing = Get-Item -LiteralPath $LinkPath -Force -ErrorAction SilentlyContinue

if ($null -eq $existing) {
    Write-Line 'No mount point yet; creating the junction.' 'Yellow'
    New-Junction -Path $LinkPath -Target $source
}
elseif (-not (Test-IsReparsePoint -Item $existing)) {
    Stop-Setup "$LinkPath is a real directory, not a junction. The script never deletes a real folder - rename or move it aside, then run again."
}
else {
    $targets = @(Get-JunctionTargets -Item $existing)
    if ($targets -contains $source) {
        Write-Line 'Junction already points at the art folder; nothing to change.' 'Green'
    }
    elseif ($Repair) {
        Write-Line "Junction points at '$($targets -join ', ')'; replacing it because -Repair was passed." 'Yellow'
        Remove-Junction -Path $LinkPath
        New-Junction -Path $LinkPath -Target $source
    }
    else {
        Stop-Setup "Junction points at '$($targets -join ', ')' instead of '$source'. Pass -Repair to replace it."
    }
}

# --- verify what is on disk now, rather than trusting the branch above --------------------
$verify = Get-Item -LiteralPath $LinkPath -Force
if (-not (Test-IsReparsePoint -Item $verify)) {
    Stop-Setup "Verification failed: '$LinkPath' is not a junction."
}

$verifyTargets = @(Get-JunctionTargets -Item $verify)
if (-not ($verifyTargets -contains $source)) {
    Stop-Setup "Verification failed: '$LinkPath' points at '$($verifyTargets -join ', ')'."
}

$entries = @(Get-ChildItem -LiteralPath $LinkPath -Force -ErrorAction SilentlyContinue)
Write-Line ("Junction OK -> {0}  ({1} top-level entries visible)" -f $source, $entries.Count) 'Green'

# --- writability probe: Unity drops a .meta file next to every imported asset --------------
$probe = Join-Path $source ('.samsara-write-probe-' + [Guid]::NewGuid().ToString('N'))
try {
    [System.IO.File]::WriteAllText($probe, 'probe')
    [System.IO.File]::Delete($probe)
    Write-Line 'Art folder is writable (Unity needs this for .meta files).' 'Green'
}
catch {
    Write-Line "Art folder is NOT writable: $($_.Exception.Message)" 'Yellow'
    Write-Line 'Unity will fail to import it. Fix the permissions, then re-run this script.' 'Yellow'
}

Write-Line ''
Write-Line 'Next steps:'
Write-Line '  1. open the project in Unity; the in-editor check is SamsaraWest/工程/检查外部素材 junction'
Write-Line '  2. bootstrap a fresh clone with the menu SamsaraWest/工程/一键初始化骨架'
Write-Line '     (headless: -executeMethod SamsaraWest.Editor.ProjectSetup.RunAll)'
Write-Line '  3. prove the project is healthy: Tools\run-tests.ps1 -Platform All'

exit 0
