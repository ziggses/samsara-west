#Requires -Version 5.1
<#
.SYNOPSIS
    Launches run-tests.ps1 in a detached process and returns immediately.

.DESCRIPTION
    A headless Unity writes its progress to -logFile rather than to stdout, so an interactive
    shell would just sit there looking idle for several minutes. This launcher hands the work to
    a separate process, redirects its output to Logs\TestResults, and lets the caller poll
    <Platform>.status.txt / the results XML instead of blocking.

    status.txt values: running | passed | failed | error

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File E:\tx2\samsara-west\Tools\start-tests.ps1 -Platform EditMode
#>
[CmdletBinding()]
param(
    [ValidateSet('EditMode', 'PlayMode', 'All')]
    [string] $Platform = 'All',

    [string] $UnityPath = 'E:\g2w\Unity\2022.3.62f3c1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'

$projectPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resultsRoot = Join-Path $projectPath 'Logs\TestResults'
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

$stdout = Join-Path $resultsRoot "$Platform.out.txt"
$stderr = Join-Path $resultsRoot "$Platform.err.txt"
$status = Join-Path $resultsRoot "$Platform.status.txt"
$wrapper = Join-Path $resultsRoot "$Platform.wrapper.ps1"
$runner = Join-Path $PSScriptRoot 'run-tests.ps1'

foreach ($file in @($stdout, $stderr, $status, $wrapper)) {
    if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
}

Set-Content -LiteralPath $status -Value 'running' -Encoding ASCII

# The wrapper is generated as a real file so quoting never has to survive a shell round trip.
$wrapperBody = @(
    '#Requires -Version 5.1',
    "`$ErrorActionPreference = 'Continue'",
    "& '$runner' -Platform $Platform -UnityPath '$UnityPath' *> '$stdout'",
    '`$code = $LASTEXITCODE',
    'switch ($code) {',
    "    0 { `$text = 'passed' }",
    "    1 { `$text = 'failed' }",
    "    default { `$text = 'error' }",
    '}',
    "Set-Content -LiteralPath '$status' -Value `$text -Encoding ASCII"
)
Set-Content -LiteralPath $wrapper -Value $wrapperBody -Encoding UTF8

$process = Start-Process -FilePath 'powershell' `
    -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $wrapper) `
    -RedirectStandardError $stderr `
    -PassThru -WindowStyle Hidden

Write-Output "started pid=$($process.Id) platform=$Platform status=$status"
