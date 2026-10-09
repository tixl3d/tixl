# Build a complete TiXL release (Player published + Editor with all dependencies)
# Used by: installer.iss (local) and .github/actions/build/action.yml (CI)
#
# Usage:
#   pwsh Installer/Windows/build-release.ps1              # full build
#   pwsh Installer/Windows/build-release.ps1 -SkipRestore # skip dotnet restore (CI may restore separately)

param([switch]$SkipRestore)

$ErrorActionPreference = "Stop"
# Windows PowerShell's progress bar slows Invoke-WebRequest down by an order of magnitude.
$ProgressPreference = "SilentlyContinue"
$root = Resolve-Path "$PSScriptRoot/../.."

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

# The Vulkan backend compiles shaders with slangc. The version comes from the compiler's pin so the two can't drift.
$slangSource = Get-Content "$root/Core/Resource/ShaderCompiling/SlangShaderCompiler.cs" -Raw
if ($slangSource -notmatch 'PinnedVersion\s*=\s*"([^"]+)"') { throw "Could not read SlangShaderCompiler.PinnedVersion" }
$slangVersion = $Matches[1]
$slangZip = "slang-$slangVersion-windows-x86_64.zip"
$deps += @{ File = $slangZip; Url = "https://github.com/shader-slang/slang/releases/download/v$slangVersion/$slangZip" }

foreach ($dep in $deps) {
    $path = Join-Path $downloadsDir $dep.File
    if (-not (Test-Path $path)) {
        Write-Host "Downloading $($dep.File)..." -ForegroundColor Cyan
        Invoke-WebRequest -Uri $dep.Url -OutFile $path
    } else {
        Write-Host "$($dep.File) already present, skipping download." -ForegroundColor DarkGray
    }
}

# Only what slangc needs for HLSL -> SPIR-V (~33 MB of the release's ~150 MB bin/; slang-llvm alone is 80 MB and
# only serves CPU targets). SlangShaderCompiler.FindCompiler looks for it in <app>/slang/bin.
Write-Host "Bundling slangc $slangVersion..." -ForegroundColor Cyan
$slangStaging = Join-Path ([System.IO.Path]::GetTempPath()) "tixl-slang-$slangVersion"
if (Test-Path $slangStaging) { Remove-Item -Recurse -Force $slangStaging }
Expand-Archive -Path (Join-Path $downloadsDir $slangZip) -DestinationPath $slangStaging
$slangTarget = "$root/Editor/bin/Release/net10.0/slang"
if (Test-Path $slangTarget) { Remove-Item -Recurse -Force $slangTarget }
New-Item -ItemType Directory -Force -Path "$slangTarget/bin" | Out-Null
foreach ($file in @("slangc.exe", "slang.dll", "slang-compiler.dll", "slang-glslang.dll", "slang-glsl-module.dll")) {
    Copy-Item "$slangStaging/bin/$file" "$slangTarget/bin/"
}
Copy-Item "$slangStaging/LICENSE" "$slangTarget/"
Remove-Item -Recurse -Force $slangStaging
if (-not (Test-Path "$slangTarget/bin/slangc.exe")) { throw "slangc.exe missing after unpacking $slangZip" }

Write-Host "Release build complete." -ForegroundColor Green
