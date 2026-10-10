<#
Blocks until measurements.csv gains a new vsync_off line (the last line a measurement writes) and the editor
has exited, then prints the new lines. Run it in the background after launching the editor.
Usage:  .\wait-for-measure.ps1 [-TimeoutMin 90]
#>
param([int] $TimeoutMin = 90)
$csv = Join-Path $PSScriptRoot 'measurements.csv'

$start = if (Test-Path $csv) { @(Get-Content $csv).Count } else { 0 }
$deadline = (Get-Date).AddMinutes($TimeoutMin)
while ((Get-Date) -lt $deadline) {
    $lines = if (Test-Path $csv) { @(Get-Content $csv) } else { @() }
    $new = $lines | Select-Object -Skip $start
    if (($new | Where-Object { $_ -match ',vsync_off,' }) -and -not (Get-Process TiXL -ErrorAction SilentlyContinue)) {
        'MEASURED'
        $new
        exit 0
    }
    Start-Sleep -Seconds 5
}
'TIMEOUT'
exit 1
