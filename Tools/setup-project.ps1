#Requires -Version 5.1
<#
.SYNOPSIS
    Brings a fresh clone to the "compiles, builds, tests" state: mounts the external art folder,
    then runs the in-editor project setup.

.DESCRIPTION
    Wraps SamsaraWest.Editor.ProjectSetup.RunAll, which configures player/render settings, imports
    the CSV tables (incremental - unchanged tables are skipped by file hash), creates the
    ScriptableObject assets, writes the bootstrap scene, registers it in the build settings and
    reports on the external art junction.

    Step 1 mounts Assets\_External to the art folder that lives outside the repository
    (Tools\setup-external-assets.ps1; idempotent, skip with -SkipExternalAssets). Step 2 runs Unity.
    On a cold Library the first run imports everything and takes minutes, so the script polls the
    log and reports progress instead of sitting silent.

    RunAll exits the editor itself in batch mode, so the process exit code is the verdict:
        0 - project initialised and validation is clean
        2 - project initialised, but data / localization validation reported errors
    Exit code 2 is not a crash: open SamsaraWest/数据/数据工具窗口, fix the offending table, re-run.
    The script additionally verifies the artifact files on disk, because a batch-mode editor that
    died early can still report a clean exit code.

    Exit codes:
        0 - the project is initialised
        1 - Unity failed (compile error, crash), the run timed out, or artifacts are missing
        2 - the external art folder could not be mounted, or validation reported errors

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File E:\tx2\samsara-west\Tools\setup-project.ps1

.EXAMPLE
    # Art lives somewhere else, and the first cold import needs a longer leash.
    powershell -NoProfile -ExecutionPolicy Bypass -File ...\setup-project.ps1 -ArtFolder 'D:\art' -TimeoutSeconds 1800
#>
[CmdletBinding()]
param(
    [string] $UnityPath = 'E:\g2w\Unity\2022.3.62f3c1\Editor\Unity.exe',

    # Art folder to mount at Assets\_External. Empty keeps the default of setup-external-assets.ps1.
    [string] $ArtFolder = '',

    # Leave the existing junction alone (useful when the art folder is on a disconnected drive).
    [switch] $SkipExternalAssets,

    # Longest the editor may take. A cold clone has to import every asset, so this is generous.
    [int] $TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$resultsRoot = Join-Path $projectPath 'Logs\TestResults'
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

function Write-Line {
    param([string] $Message, [string] $Colour = 'Gray')
    Write-Host "[setup] $Message" -ForegroundColor $Colour
}

function Stop-Setup {
    param([string] $Message, [int] $Code = 1)
    Write-Line $Message 'Red'
    exit $Code
}

if (-not (Test-Path -LiteralPath $UnityPath)) {
    Stop-Setup "Unity editor not found at '$UnityPath'. Pass -UnityPath explicitly."
}

# --- step 1: the art mount -----------------------------------------------------------------
if ($SkipExternalAssets) {
    Write-Line 'Skipping the external art mount (-SkipExternalAssets).'
}
else {
    $mountScript = Join-Path $PSScriptRoot 'setup-external-assets.ps1'
    if (-not (Test-Path -LiteralPath $mountScript)) {
        Stop-Setup "Missing '$mountScript'." 2
    }

    $mountArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $mountScript)
    if ($ArtFolder) { $mountArguments += @('-SourcePath', $ArtFolder) }

    $LASTEXITCODE = 0
    & powershell.exe @mountArguments
    if ($LASTEXITCODE -ne 0) {
        # The mount script is idempotent and only exits non-zero when a human has to decide
        # something (wrong target, real directory in the way, art folder unreadable).
        Stop-Setup "External art is not mounted (exit $LASTEXITCODE). Fix the report above, then re-run." 2
    }
}

# --- step 2: the editor run ----------------------------------------------------------------
# Two headless editors on one project corrupt the Library, so refuse to start while another
# SamsaraWest editor is alive.
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like "*$projectPath*" })
if ($running.Count -gt 0) {
    Stop-Setup "Another Unity process already owns this project (PID $($running.ProcessId -join ', ')). Aborting."
}

$logFile = Join-Path $resultsRoot 'setup-project.log'
if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }

$arguments = @(
    '-batchmode', '-nographics', '-quit',
    '-projectPath', $projectPath,
    '-executeMethod', 'SamsaraWest.Editor.ProjectSetup.RunAll',
    '-logFile', $logFile
)

Write-Line "Unity $($arguments -join ' ')" 'DarkGray'
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -PassThru -NoNewWindow

$started = Get-Date
$deadline = $started.AddSeconds($TimeoutSeconds)
$nextReport = $started.AddSeconds(15)
while (-not $process.HasExited) {
    if ((Get-Date) -gt $deadline) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        Stop-Setup "Unity did not finish within $TimeoutSeconds s and was killed. Inspect '$logFile', or run Tools\diag-unity.ps1 -LogFile '$logFile' to see whether it is stuck in an import loop."
    }

    if ((Get-Date) -gt $nextReport) {
        # Log growth is the cheapest liveness signal: a cold import writes megabytes, a hung
        # editor stops writing while still burning CPU.
        $state = 'no log yet'
        if (Test-Path -LiteralPath $logFile) {
            $info = Get-Item -LiteralPath $logFile
            $idle = [int] ((Get-Date) - $info.LastWriteTime).TotalSeconds
            $state = "{0:N1} MB, last write {1}s ago" -f ($info.Length / 1MB), $idle
        }

        Write-Line ("still running: elapsed {0}s of {1}s, log {2}" -f `
                [int] ((Get-Date) - $started).TotalSeconds, $TimeoutSeconds, $state) 'DarkGray'
        $nextReport = (Get-Date).AddSeconds(15)
    }

    Start-Sleep -Seconds 2
    $process.Refresh()
}

$exitCode = 1
try {
    $process.WaitForExit()
    $exitCode = [int] $process.ExitCode
}
catch {
    Write-Line "Could not read the editor exit code: $($_.Exception.Message)" 'Yellow'
}

# A compile error means RunAll never ran: whatever sits on disk is left over from an earlier run, so
# listing those files as "OK" would be a lie. Fail before the artifact table, not after it.
$compileErrors = @(Select-String -LiteralPath $logFile -Pattern 'error CS' -SimpleMatch -ErrorAction SilentlyContinue)
if ($compileErrors.Count -gt 0) {
    Write-Line "The editor did not compile, so the project was never initialised ($($compileErrors.Count) errors):" 'Red'
    foreach ($compileError in $compileErrors | Select-Object -First 10) {
        Write-Line "  $($compileError.Line.Trim())" 'DarkRed'
    }

    Stop-Setup "Fix the compile errors, then re-run. Log: $logFile" 1
}

# --- artifacts: the only proof that RunAll actually produced the project -------------------
$expected = [ordered] @{
    'data catalog'        = 'Assets\_Project\Data\Generated\DefinitionCatalog.asset'
    'battle config'       = 'Assets\_Project\Battle\Config\BattleConfig_Default.asset'
    'localization table'  = 'Assets\_Project\Localization\Generated\LocalizationTable_zh-Hans.asset'
    'bootstrap scene'     = 'Assets\_Project\Flow\Scenes\Bootstrap.unity'
}

$missing = @()
foreach ($entry in $expected.GetEnumerator()) {
    $path = Join-Path $projectPath $entry.Value
    if (Test-Path -LiteralPath $path) {
        Write-Line ("OK      {0,-19} {1}" -f $entry.Key, $entry.Value) 'Green'
    }
    else {
        Write-Line ("MISSING {0,-19} {1}" -f $entry.Key, $entry.Value) 'Red'
        $missing += $entry.Key
    }
}

$report = @()
if (Test-Path -LiteralPath $logFile) {
    $report = @(Select-String -LiteralPath $logFile -Pattern '[SamsaraWest]' -SimpleMatch -ErrorAction SilentlyContinue)
}

if ($report.Count -gt 0) {
    Write-Line '--- editor report ---' 'DarkGray'
    foreach ($line in $report) { Write-Host "  $($line.Line.Trim())" -ForegroundColor DarkGray }
}

# The raw tail only earns its screen space when something went wrong: a healthy editor exit dumps
# megabytes of allocator statistics that bury the few lines a human actually reads.
if ($exitCode -ne 0 -or $report.Count -eq 0) {
    Write-Line '--- last lines of the editor log ---' 'DarkGray'
    if (Test-Path -LiteralPath $logFile) {
        Get-Content -LiteralPath $logFile -Tail 25 | ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }
    }
    else {
        Write-Line "no log at $logFile" 'Yellow'
    }
}

if ($exitCode -ne 0) {
    if ($exitCode -eq 2) {
        Stop-Setup "Initialised with validation errors (Unity exit 2). Fix the reported tables (SamsaraWest/数据/数据工具窗口), then re-run. Log: $logFile" 2
    }

    Stop-Setup "Unity exited with $exitCode. Log: $logFile" 1
}

if ($compileErrors.Count -gt 0) {
    Stop-Setup 'Unity reported a clean exit but the log holds compile errors.' 1
}

if ($missing.Count -gt 0) {
    Stop-Setup "Unity exited cleanly but these artifacts are missing: $($missing -join ', ')." 1
}

Write-Line ''
Write-Line 'Project initialised. Next steps:' 'Green'
Write-Line '  Tools\run-tests.ps1 -Platform All    # prove the project is still green'
Write-Line '  menu SamsaraWest/出包/Windows x64     # produce a build'

exit 0
