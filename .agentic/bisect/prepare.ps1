<#
One step per bisect commit: hard checkout, clean (keeps Installer/ and local tool config), fix folder casing,
NuGet restore, inject the measure probe, Debug build, start the editor.
Usage:  .\prepare.ps1 <sha> [-NoLaunch]
#>
param(
    [Parameter(Mandatory, Position = 0)] [string] $Commit,
    [switch] $NoLaunch
)
$ErrorActionPreference = 'Stop'
$repoFile = Join-Path $PSScriptRoot 'repo.txt'
if (-not (Test-Path $repoFile)) { throw 'repo.txt missing - run .agentic\bisect\install.ps1 from the repository first.' }
$Repo = (Get-Content $repoFile -TotalCount 1).Trim()

function Step($msg) { Write-Host "`n== $msg" -ForegroundColor Cyan }

Push-Location $Repo
try {
    if (Get-Process TiXL -ErrorAction SilentlyContinue) { throw 'TiXL is running - close it first.' }

    Step "hard checkout $Commit"
    git -c core.safecrlf=false checkout -q -f --detach $Commit 2>$null
    if ($LASTEXITCODE -ne 0) { throw "checkout failed (exit $LASTEXITCODE) - retry; a locked .git\index is the usual cause" }
    git log -1 --format='%h %cs %s'

    Step 'clean (keeps Installer/, .claude/, .idea/, .vs/)'
    $removed = git clean -fdx -e Installer/ -e .claude/ -e .idea/ -e .vs/ 2>$null
    "removed $(@($removed).Count) entries"

    Step 'fix folder casing to match git (case-only renames are not applied on Windows)'
    $tracked = git ls-files Operators | ForEach-Object { ($_ -split '/')[1] } | Sort-Object -Unique -CaseSensitive
    foreach ($dir in Get-ChildItem Operators -Directory) {
        $want = $tracked | Where-Object { $_ -ieq $dir.Name -and $_ -cne $dir.Name } | Select-Object -First 1
        if (-not $want) { continue }
        $renamed = $false
        for ($attempt = 1; $attempt -le 5 -and -not $renamed; $attempt++) {
            try {
                Rename-Item $dir.FullName "$want.casefix"
                Rename-Item (Join-Path 'Operators' "$want.casefix") $want
                $renamed = $true
                "renamed $($dir.Name) -> $want"
            } catch {
                Start-Sleep -Seconds 2
            }
        }
        if (-not $renamed) { Write-Warning "could not rename Operators\$($dir.Name) -> $want (folder in use, e.g. by Rider); continuing" }
    }

    $left = git -c core.safecrlf=false status --porcelain --ignored 2>$null | Where-Object { $_ -notmatch '^!! (Installer/|\.claude/|\.idea/|\.vs/)' }
    if ($left) { throw "tree not clean after clean:`n$($left -join "`n")" }

    Step 'nuget restore'
    dotnet restore t3.sln --nologo -v q 2>&1 | Where-Object { $_ -notmatch 'NU1701' } | Select-Object -Last 5
    if ($LASTEXITCODE -ne 0) { throw 'restore failed' }

    Step 'inject measure probe'
    & (Join-Path $PSScriptRoot 'inject-probe.ps1')

    Step 'Debug build'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    dotnet build t3.sln -c Debug -nologo -v q -clp:ErrorsOnly 2>&1 | Select-Object -Last 15
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    'build ok in {0:0}s' -f $sw.Elapsed.TotalSeconds

    if (-not $NoLaunch) {
        Step 'start editor'
        $exe = Get-ChildItem 'Editor\bin\Debug' -Recurse -Filter 'TiXL.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        Start-Process -FilePath $exe.FullName -WorkingDirectory $exe.DirectoryName | Out-Null
        "started $($exe.FullName)"
    }
} finally {
    Pop-Location
}
