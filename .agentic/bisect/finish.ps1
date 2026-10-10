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
    # Same transient-lock handling as prepare.ps1: retry, and don't treat git warnings as failures.
    $isOnMain = $false
    for ($attempt = 1; $attempt -le 3 -and -not $isOnMain; $attempt++) {
        $ErrorActionPreference = 'Continue'
        $gitOutput = git -c core.safecrlf=false checkout -q -f main 2>&1
        $ErrorActionPreference = 'Stop'
        $isOnMain = $LASTEXITCODE -eq 0 -and (git rev-parse --abbrev-ref HEAD).Trim() -eq 'main'
        if (-not $isOnMain) {
            Write-Warning "checkout attempt $attempt failed: $(($gitOutput | ForEach-Object { "$_" }) -join ' | ')"
            Start-Sleep -Seconds 3
        }
    }
    if (-not $isOnMain) { throw 'checkout of main failed 3 times' }
    $probe = 'Editor\AgentFrameProbe.cs'
    if (Test-Path $probe) { Remove-Item $probe }
    "Back on main: $(git log -1 --format='%h %cs %s')"
    git -c core.safecrlf=false status --porcelain 2>$null
} finally {
    Pop-Location
}
