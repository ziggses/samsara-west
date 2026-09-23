#Requires -Version 5.1
<#
.SYNOPSIS
    Read-only diagnostics for a running headless Unity test run.
.DESCRIPTION
    Prints the editor processes, how fast they are burning CPU, and how often the log repeats
    "Start importing ...", which is the signature of an asset import loop.
#>
[CmdletBinding()]
param(
    [string] $LogFile = 'E:\tx2\samsara-west\Logs\TestResults\EditMode.log'
)

$ErrorActionPreference = 'Continue'

Write-Output '--- processes ---'
Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -match 'Unity|AssetImportWorker|VBCSCompiler|csc|dotnet' } |
    Select-Object Id, ProcessName, CPU, StartTime |
    Format-Table -AutoSize | Out-String -Width 200 | Write-Output

if (-not (Test-Path -LiteralPath $LogFile)) {
    Write-Output "no log at $LogFile"
    exit 0
}

$info = Get-Item -LiteralPath $LogFile
Write-Output "--- log ---"
Write-Output "size=$($info.Length) lastWrite=$($info.LastWriteTime)"

$lines = Get-Content -LiteralPath $LogFile
Write-Output "lineCount=$($lines.Count)"

$imports = @($lines | Select-String -SimpleMatch 'Start importing ')
Write-Output "importStarts=$($imports.Count)"

Write-Output '--- most repeated import targets ---'
$imports |
    ForEach-Object { ($_.Line -split 'Assets/')[1] } |
    ForEach-Object { ($_ -split ' using Guid')[0] } |
    Group-Object |
    Sort-Object Count -Descending |
    Select-Object -First 8 Count, Name |
    Format-Table -AutoSize | Out-String -Width 200 | Write-Output

Write-Output '--- last 30 lines ---'
$lines | Select-Object -Last 30 | Write-Output
