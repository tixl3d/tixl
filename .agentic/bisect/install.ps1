<#
Copies the bisect tools out of the working tree (checkouts and git clean would delete them mid-bisect)
and records which repository they operate on. Run from the repository on main.
Usage:  .agentic\bisect\install.ps1 [-Target <folder>]     # default: <repo>\..\_bisect
#>
param([string] $Target)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Target) { $Target = Join-Path (Split-Path $repo -Parent) '_bisect' }
$Target = [IO.Path]::GetFullPath($Target)
if ($Target.StartsWith($repo + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Target must be outside the repository: $Target" }

New-Item -ItemType Directory -Force $Target | Out-Null
foreach ($f in 'AgentFrameProbe.cs', 'prepare.ps1', 'inject-probe.ps1', 'wait-for-measure.ps1', 'finish.ps1') {
    Copy-Item (Join-Path $PSScriptRoot $f) (Join-Path $Target $f) -Force
}
Set-Content (Join-Path $Target 'repo.txt') $repo
"Bisect tools installed to $Target for $repo"
"Next: $Target\prepare.ps1 <sha>"
