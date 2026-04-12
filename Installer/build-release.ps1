# Build a complete TiXL release (Player published + Editor with all dependencies)
# Used by: installer.iss (local) and .github/actions/build/action.yml (CI)
#
# Usage:
#   pwsh Installer/build-release.ps1              # full build
#   pwsh Installer/build-release.ps1 -SkipRestore # skip dotnet restore (CI may restore separately)

param([switch]$SkipRestore)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot/.."

if (-not $SkipRestore) {
    Write-Host "Restoring dependencies..." -ForegroundColor Cyan
    dotnet restore "$root/t3.sln"
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }
}

Write-Host "Publishing Player (self-contained, win-x64)..." -ForegroundColor Cyan
dotnet publish "$root/Player/Player.csproj" -c Release -p:PublishProfile=FolderProfile
if ($LASTEXITCODE -ne 0) { throw "dotnet publish Player failed" }

Write-Host "Building solution in Release mode..." -ForegroundColor Cyan
dotnet build "$root/t3.sln" -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

# Download installer dependencies if missing
$downloadsDir = "$PSScriptRoot/dependencies/downloads"
New-Item -ItemType Directory -Force -Path $downloadsDir | Out-Null

$deps = @(
    @{ File = "dotnet-sdk-10.0.201-win-x64.exe"; Url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.201/dotnet-sdk-10.0.201-win-x64.exe" },
    @{ File = "VC_redist.x64.exe";              Url = "https://aka.ms/vs/17/release/vc_redist.x64.exe" }
)

foreach ($dep in $deps) {
    $path = Join-Path $downloadsDir $dep.File
    if (-not (Test-Path $path)) {
        Write-Host "Downloading $($dep.File)..." -ForegroundColor Cyan
        Invoke-WebRequest -Uri $dep.Url -OutFile $path
    } else {
        Write-Host "$($dep.File) already present, skipping download." -ForegroundColor DarkGray
    }
}

Write-Host "Release build complete." -ForegroundColor Green
