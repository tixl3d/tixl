<#
Ends a bisect: forced checkout of main and removal of the probe. Discards all local changes in tracked files.
Usage:  .\finish.ps1
#>
$ErrorActionPreference = 'Stop'
$repoFile = Join-Path $PSScriptRoot 'repo.txt'
if (-not (Test-Path $repoFile)) { throw 'repo.txt missing - run .agentic\bisect\install.ps1 from the repository first.' }
$Repo = (Get-Content $repoFile -TotalCount 1).Trim()

Push-Location $Repo
try {
    if (Get-Process TiXL -ErrorAction SilentlyContinue) { throw 'TiXL is running - close it first.' }
    git -c core.safecrlf=false checkout -q -f main 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'checkout of main failed - retry' }
    $probe = 'Editor\AgentFrameProbe.cs'
    if (Test-Path $probe) { Remove-Item $probe }
    "Back on main: $(git log -1 --format='%h %cs %s')"
    git -c core.safecrlf=false status --porcelain 2>$null
} finally {
    Pop-Location
}
