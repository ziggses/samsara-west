#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the SamsaraWest EditMode / PlayMode test suites through a headless Unity.

.DESCRIPTION
    Unity cannot open the same project twice at the same time, so this script is intentionally
    serial: it runs one platform at a time and waits for the editor process to exit before
    moving on.

    Headless Unity on Windows sometimes finishes the run, writes the NUnit XML and then hangs
    forever inside EditorApplication.Exit. To keep CI from waiting for nothing, every run has a
    timeout: once the deadline is reached and the result file parses as a complete test run, the
    editor is killed and the run counts as finished. A deadline with no usable result is a hard
    failure (exit code 2).

    Exit codes:
        0  - every discovered test passed
        1  - at least one test failed / was inconclusive
        2  - Unity itself failed to run the suite (compile error, crash, missing results)

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File E:\tx2\samsara-west\Tools\run-tests.ps1
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File ...\run-tests.ps1 -Platform EditMode
.EXAMPLE
    # Unity only honours a single -testFilter, so this parameter takes a single name as well.
    powershell -NoProfile -ExecutionPolicy Bypass -File ...\run-tests.ps1 -Platform EditMode -TestFilter SamsaraWest.Tests.EditMode.ObjectPoolTests
#>
[CmdletBinding()]
param(
    [ValidateSet('EditMode', 'PlayMode', 'All')]
    [string] $Platform = 'All',

    [string] $UnityPath = 'E:\g2w\Unity\2022.3.62f3c1\Editor\Unity.exe',

    # Skip the suites and only prove the project still compiles.
    [switch] $CompileOnly,

    # Run a single fixture / test by full name. Used to hunt down one slow or hanging test
    # without re-running (and re-waiting for) the whole suite.
    [string] $TestFilter = '',

    # Longest a single Unity invocation may take before it is treated as stuck.
    [int] $TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$projectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resultsRoot = Join-Path $projectPath 'Logs\TestResults'
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

if (-not (Test-Path -LiteralPath $UnityPath)) {
    Write-Error "Unity editor not found at '$UnityPath'. Pass -UnityPath explicitly."
    exit 2
}

# Refuse to start a second headless editor on the same project: it would corrupt the Library.
$running = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -and $_.CommandLine -like "*$projectPath*" })
if ($running.Count -gt 0) {
    Write-Host "[run-tests] Another Unity process already owns this project (PID $($running.ProcessId -join ', ')). Aborting." -ForegroundColor Red
    exit 2
}

function Test-ResultFile {
    <#
        A trustworthy result file has to parse and carry the <test-run> root node: Unity writes it
        in place, so a half-written file would otherwise pass a Test-Path check.
    #>
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) { return $false }

    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.Load($Path)
        return $null -ne $xml.SelectSingleNode('test-run')
    }
    catch {
        return $false
    }
}

function Invoke-Unity {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $LogFile,
        [string] $ResultsFile = ''
    )

    if (Test-Path -LiteralPath $LogFile) { Remove-Item -LiteralPath $LogFile -Force }
    if ($ResultsFile -and (Test-Path -LiteralPath $ResultsFile)) { Remove-Item -LiteralPath $ResultsFile -Force }

    Write-Host "[run-tests] Unity $($Arguments -join ' ')" -ForegroundColor DarkGray
    $process = Start-Process -FilePath $UnityPath -ArgumentList $Arguments -PassThru -NoNewWindow

    # Wait for either a clean exit or a complete result file. The result file is the real signal:
    # once it holds a parsed <test-run>, the suite is over and a hang can only be in the editor's
    # exit path, which is not worth waiting for.
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $resultsReady = $false
    while (-not $process.HasExited) {
        if ($ResultsFile -and (Test-ResultFile -Path $ResultsFile)) {
            $resultsReady = $true
            break
        }

        if ((Get-Date) -gt $deadline) {
            Write-Host "[run-tests] Unity still alive after $TimeoutSeconds s, checking results..." -ForegroundColor Yellow
            break
        }

        Start-Sleep -Seconds 2
        $process.Refresh()
    }

    if (-not $process.HasExited) {
        # Give the editor a short grace period to shut down on its own before killing it.
        $graceDeadline = (Get-Date).AddSeconds(20)
        while (-not $process.HasExited -and (Get-Date) -lt $graceDeadline) {
            Start-Sleep -Seconds 2
            $process.Refresh()
        }
    }

    if ($process.HasExited) {
        # ExitCode is occasionally still unset right after HasExited flips; WaitForExit settles it.
        try {
            $process.WaitForExit()
            return [int] $process.ExitCode
        }
        catch {
            return -1
        }
    }

    if ($resultsReady -or ($ResultsFile -and (Test-ResultFile -Path $ResultsFile))) {
        Write-Host "[run-tests] Result file is complete; treating the stuck exit as finished (exit code 0)." -ForegroundColor Yellow
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        return 0
    }

    Write-Host "[run-tests] Timed out with no usable result file; killing the editor." -ForegroundColor Red
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    return -1
}

function Read-TestRun {
    param([Parameter(Mandatory)][string] $ResultsFile)

    if (-not (Test-Path -LiteralPath $ResultsFile)) {
        return $null
    }

    [xml] $xml = Get-Content -LiteralPath $ResultsFile -Raw -Encoding UTF8
    $run = $xml.'test-run'
    if ($null -eq $run) {
        return $null
    }

    return [pscustomobject] @{
        Total        = [int] $run.total
        Passed       = [int] $run.passed
        Failed       = [int] $run.failed
        Inconclusive = [int] $run.inconclusive
        Skipped      = [int] $run.skipped
        Duration     = [double] $run.duration
        Result       = [string] $run.result
    }
}

function Show-Failures {
    param([Parameter(Mandatory)][string] $ResultsFile)

    [xml] $xml = Get-Content -LiteralPath $ResultsFile -Raw -Encoding UTF8
    $cases = @($xml.SelectNodes("//test-case[@result='Failed' or @result='Inconclusive']"))
    foreach ($case in $cases) {
        Write-Host ("  [FAIL] {0}" -f $case.fullname) -ForegroundColor Red
        $message = $case.SelectSingleNode('failure/message')
        if ($message -and $message.InnerText) {
            $firstLine = ($message.InnerText -split "`r?`n")[0]
            Write-Host ("         {0}" -f $firstLine.Trim()) -ForegroundColor DarkRed
        }
    }

    return $cases.Count
}

$platforms = @($Platform)
if ($Platform -eq 'All') { $platforms = @('EditMode', 'PlayMode') }

if ($CompileOnly) {
    $logFile = Join-Path $resultsRoot 'compile.log'
    $arguments = @(
        '-batchmode', '-nographics', '-quit',
        '-projectPath', $projectPath,
        '-logFile', $logFile
    )
    $code = Invoke-Unity -Arguments $arguments -LogFile $logFile
    $errors = @(Select-String -LiteralPath $logFile -Pattern 'error CS' -SimpleMatch -ErrorAction SilentlyContinue)
    if ($errors.Count -gt 0) {
        Write-Host '[run-tests] Compile errors:' -ForegroundColor Red
        foreach ($error in $errors) { Write-Host "  $($error.Line)" -ForegroundColor DarkRed }
        exit 2
    }

    Write-Host "[run-tests] Compile OK (Unity exit $code)." -ForegroundColor Green
    exit 0
}

$failedSuites = 0
$summary = @()

foreach ($current in $platforms) {
    $resultsFile = Join-Path $resultsRoot "$current.xml"
    $logFile = Join-Path $resultsRoot "$current.log"

    $arguments = @(
        '-batchmode', '-nographics',
        '-projectPath', $projectPath,
        '-runTests',
        '-testPlatform', $current,
        '-testResults', $resultsFile,
        '-logFile', $logFile
    )

    if ($TestFilter) { $arguments += @('-testFilter', $TestFilter) }

    $exitCode = Invoke-Unity -Arguments $arguments -LogFile $logFile -ResultsFile $resultsFile
    $run = Read-TestRun -ResultsFile $resultsFile

    if ($null -eq $run) {
        Write-Host "[run-tests] $current produced no results (Unity exit $exitCode). See $logFile" -ForegroundColor Red
        $compileErrors = @(Select-String -LiteralPath $logFile -Pattern 'error CS' -SimpleMatch -ErrorAction SilentlyContinue)
        foreach ($error in $compileErrors) { Write-Host "  $($error.Line)" -ForegroundColor DarkRed }
        $failedSuites++
        continue
    }

    $summary += [pscustomobject] @{ Platform = $current; Run = $run }

    $color = 'Green'
    if ($run.Failed -gt 0 -or $run.Inconclusive -gt 0) { $color = 'Red' }
    Write-Host ("[run-tests] {0}: {1}/{2} passed, {3} failed, {4} inconclusive, {5} skipped in {6:N1}s (Unity exit {7})" -f `
        $current, $run.Passed, $run.Total, $run.Failed, $run.Inconclusive, $run.Skipped, $run.Duration, $exitCode) `
        -ForegroundColor $color

    if ($run.Failed -gt 0 -or $run.Inconclusive -gt 0) {
        [void] (Show-Failures -ResultsFile $resultsFile)
        $failedSuites++
    }
}

Write-Host ''
Write-Host '=============== TEST SUMMARY ===============' -ForegroundColor Cyan
foreach ($entry in $summary) {
    $run = $entry.Run
    Write-Host ("  {0,-9} total {1,3}  passed {2,3}  failed {3,3}  inconclusive {4,3}  skipped {5,3}" -f `
        $entry.Platform, $run.Total, $run.Passed, $run.Failed, $run.Inconclusive, $run.Skipped)
}

Write-Host "  results: $resultsRoot"
Write-Host '============================================' -ForegroundColor Cyan

if ($failedSuites -gt 0) { exit 1 }
exit 0
