<#
Injects the measure probe and its "Start Measure" app-bar button into the commit that is checked out now.
Other local changes (op fixes, an experimental fix) are left alone; a previous injection is replaced.
Usage:  .\inject-probe.ps1 [-Label <text>]    # -Label replaces the commit subject in the results, e.g. "fix1 latency=1"
#>
param([string] $Label)
$ErrorActionPreference = 'Stop'
$repoFile = Join-Path $PSScriptRoot 'repo.txt'
if (-not (Test-Path $repoFile)) { throw 'repo.txt missing - run .agentic\bisect\install.ps1 from the repository first.' }
$Repo = (Get-Content $repoFile -TotalCount 1).Trim()
$probeTarget = 'Editor\AgentFrameProbe.cs'

Push-Location $Repo
try {
    if (Get-Process TiXL -ErrorAction SilentlyContinue) { throw 'TiXL is running - close it first.' }

    # Replace an earlier injection rather than stacking a second one.
    foreach ($f in git -c core.safecrlf=false diff --name-only 2>$null) {
        if ($f -like '*WindowsUiContentDrawer.cs' -or $f -like '*AppMenuBar.cs') { git checkout -q -- $f }
    }

    $sha = (git rev-parse --short=9 HEAD).Trim()
    $date = (git show -s --format=%cs HEAD).Trim()
    $subject = $(if ($Label) { $Label } else { (git show -s --format=%s HEAD).Trim() }) -replace '[,"\\]', ' '
    if ($Label) { $sha += '+' + ($Label -replace '[^A-Za-z0-9]+', '-').Trim('-') }

    $probe = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AgentFrameProbe.cs'))
    $probe = $probe.Replace('__COMMIT_INFO__', "$sha,$date,$subject").Replace('__OUTPUT_FOLDER__', $PSScriptRoot)
    [IO.File]::WriteAllText((Join-Path $Repo $probeTarget), $probe)

    # Timing hooks around the frame start and Present.
    $drawer = Get-ChildItem Editor -Recurse -Filter 'WindowsUiContentDrawer.cs' | Select-Object -First 1
    $text = [IO.File]::ReadAllText($drawer.FullName)
    $nNewFrame = [regex]::Matches($text, '(?m)^(\s*)ImGui\.NewFrame\(\);').Count
    $nPresent = [regex]::Matches($text, '(?m)^(\s*)(ProgramWindows\.Present\(.*\);)').Count
    if ($nNewFrame -ne 1 -or $nPresent -ne 1) { throw "hook sites not unique in $($drawer.Name): NewFrame=$nNewFrame Present=$nPresent" }
    $text = [regex]::Replace($text, '(?m)^(\s*)ImGui\.NewFrame\(\);', '$1AgentFrameProbe.OnNewFrame(); ImGui.NewFrame();')
    $text = [regex]::Replace($text, '(?m)^(\s*)(ProgramWindows\.Present\(.*\);)', '$1AgentFrameProbe.BeforePresent(); $2 AgentFrameProbe.AfterPresent();')
    [IO.File]::WriteAllText($drawer.FullName, $text)

    # App-bar button.
    $menu = Join-Path $Repo 'Editor\Gui\AppMenuBar.cs'
    $text = [IO.File]::ReadAllText($menu)
    $nAnchor = [regex]::Matches($text, '(?m)^(\s*)T3Metrics\.DrawRenderPerformanceGraph\(\);').Count
    if ($nAnchor -ne 1) { throw "app-bar anchor not unique in AppMenuBar.cs: $nAnchor" }
    $text = [regex]::Replace($text, '(?m)^(\s*)T3Metrics\.DrawRenderPerformanceGraph\(\);', '$1T3Metrics.DrawRenderPerformanceGraph(); AgentFrameProbe.DrawMeasureButton();')
    [IO.File]::WriteAllText($menu, $text)

    "Instrumented $sha  $date  $subject"
    "Results append to: $(Join-Path $PSScriptRoot 'measurements.csv')"
} finally {
    Pop-Location
}
