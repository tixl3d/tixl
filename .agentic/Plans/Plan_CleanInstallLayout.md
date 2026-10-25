# Plan: Clean install layout (launcher on top, binaries in `app/`)

Status: proposal (2026-10-25). Launcher mechanism verified with a spike on Linux (framework-dependent and
self-contained); not yet verified on Windows.

## Problem

A Release install shows ~135 top-level entries on Linux and ~115 on Windows: managed DLLs, native libraries,
`.pdb`s, `deps.json`, `runtimeconfig.json`, `runtimes/`, `slang/`, `Player/`, `EditorResources/`. The one file a
user wants — `TiXL` / `TiXL.exe` — is buried in the middle. It looks unfinished and makes "which file do I
start?" a real question for portable and tarball users.

## Target layout

```
TiXL 4.4.0.3/                     tixl-4.4.0.3-linux-x64/
  TiXL.exe        launcher          TiXL                   launcher
  LICENSE.txt                       LICENSE.txt
  licenses/                         licenses/
  app/            everything else   DEPENDENCIES.md
                                    install-dependencies.sh
                                    share/                 desktop entry + icon (distro recipes)
                                    app/                   everything else
```

`app/` is today's `Editor/bin/Release/net10.0` output, unchanged. Dev builds (`Editor/bin/Debug`, Rider) keep the
flat layout; only packaging produces the new one. macOS is out of scope — the `.app` bundle already hides the
payload in `Contents/Resources/TiXL`.

## Mechanism

The .NET apphost embeds the relative path of the app DLL it starts, and that path may contain a folder. The SDK's
own `CreateAppHost` task writes it, so no binary patching is involved:

```xml
<CreateAppHost AppHostSourcePath="$(AppHostSourcePath)"
               AppHostDestinationPath="$(TixlLauncherDir)/$(AssemblyName)$(_NativeExecutableExtension)"
               AppBinaryName="app/$(AssemblyName)$(TargetExt)"
               IntermediateAssembly="@(IntermediateAssembly->'%(FullPath)')"
               WindowsGraphicalUserInterface="$(_UseWindowsGraphicalUserInterface)" />
```

Spike results (Linux, SDK 10.0.112):
- Launcher at the top runs `app/Spike.dll`; `AppContext.BaseDirectory` and the assembly `Location` both resolve
  to `app/`.
- Self-contained (`-r linux-x64 --self-contained`, as `TixlEditorRuntime` does for the tarball): the launcher
  finds `libhostfxr.so` inside `app/` and runs with `DOTNET_ROOT` unset.

The apphost is not a separate process — it loads the runtime in-process. Process name, window icon, taskbar
grouping, firewall rules and `Environment.ProcessPath` all refer to the top-level launcher.

### Why nothing in TiXL needs to change

Every runtime lookup is relative to `AppContext.BaseDirectory` or to `Core.dll`'s own location, and both are
`app/`:
- `FileLocations.StartFolder` → `EditorResources/`, `.tixl/` read-only settings, `Player/` for export.
- `RuntimeAssemblies.CoreDirectory` → `T3_ASSEMBLY_PATH` (`ProjectSetup`), which the generated operator
  `.csproj` files use for their `Core.dll` / `Logging.dll` / `Graphics*.dll` references. Existing user projects
  keep compiling.
- `TixlAssemblyLoadContext` native fallbacks (`MainDirectory`, `runtimes/<rid>/native`), `SlangShaderCompiler`
  (`slang/bin`), `VulkanLoader`, `DanceAiPhaseTracker`, BASS presence check in `AudioMixerManager`.
- `EditorRestart` relaunches `Environment.ProcessPath`, which is the launcher — correct.
- No live code depends on the current working directory.

### Rejected alternative: keep `TiXL` flat, move DLLs into `lib/`

Needs a rewritten `TiXL.deps.json` (the host refuses to start when a listed assembly is missing), an
`AssemblyLoadContext.Default.Resolving` hook installed before any dependent type is touched, native search-path
fixes for libraries that load other native libraries (OpenCV, ONNX Runtime — `AddDllDirectory` on Windows,
RPATH on Linux), and care around Roslyn/MSBuild loading and the custom load context. Many failure modes for the
same visual result.

## Implementation

### 1. Launcher target in `Editor/Editor.csproj`

Add a target `CreateTixlLauncher` (not hooked to `AfterTargets="Build"`; packaging invokes it explicitly):
- `DependsOnTargets="_GetAppHostPaths;_GetAppHostCreationConfiguration"`, then the `CreateAppHost` call above.
- Input property `TixlLauncherDir`; error out when it is empty.
- The packaging scripts call it after the Release build with the same `TixlEditorRuntime` they built with, so
  `AppHostSourcePath` resolves to the matching RID's apphost:
  `dotnet msbuild Editor/Editor.csproj -t:CreateTixlLauncher -p:Configuration=Release -p:TixlLauncherDir=<stage> [-p:TixlEditorRuntime=<rid>]`
- `_GetAppHostPaths` and `_GetAppHostCreationConfiguration` are SDK-internal (underscore) targets. Acceptable
  for a packaging-only target; if a future SDK renames them, packaging fails loudly, not the app.

### 2. Linux tarball — `Installer/Linux/build-tarball.sh`

- Stage the build into `$stage/app/` instead of `$stage/`. The `.meta` copy, proxy/waveform cleanup, runtime
  pruning and PE-DLL stripping then run on `$stage/app/` (the `find` calls are recursive already; the
  `Operators/<package>/.meta` destination moves under `app/`).
- Create the launcher into `$stage/`, move `app/licenses` to `$stage/licenses`.
- `LICENSE.txt`, `DEPENDENCIES.md`, `install-dependencies.sh`, `share/` stay at the top as today.
- Update the `-x "$build_dir/TiXL"` sanity check to also assert `$stage/TiXL` and `$stage/app/TiXL.dll`.

Distro recipes consume this stage (`--stage-only`) and start `<dir>/TiXL`, which is now the launcher:
- **AUR** (`PKGBUILD`): `ln -s /usr/lib/tixl/TiXL /usr/bin/tixl` — the apphost resolves its real path via
  `/proc/self/exe`, so the symlink works. Licences are taken from `Dependencies/licenses` directly. No change.
- **Flatpak** (`app.tixl.TiXL.yml`): `exec /app/lib/tixl/TiXL` and the `rm` of top-level helper files still
  match. No change. CI rebuilds the Flatpak from the extracted tarball, which has the same layout.
- **Nix** (`flake.nix`): `wrapDotnetProgram $out/lib/tixl/TiXL` wraps the launcher; `--system-dotnet` means the
  launcher is framework-dependent and needs `DOTNET_ROOT`, exactly like today's flat apphost. Verify in the
  `nix-build` workflow.

### 3. Windows installer — `build-release.ps1` + `installer.iss`

- `build-release.ps1`: bundle slang into `Editor/bin/Release/net10.0/slang` as today, then run the launcher
  target into `Installer/Windows/Output/launcher/`.
- `installer.iss` `[Files]`: build output and the two `.meta` sources go to `{app}\app\...`; the launcher to
  `{app}`; `licenses` to `{app}\licenses`; add `LICENSE.txt` to `{app}`.
- `UninstallFilesDir={app}\app` so `unins000.exe/.dat` don't sit at the top.
- `[InstallDelete]` for a flat install of the same version being overwritten: delete `{app}\*.dll`,
  `{app}\*.pdb`, `{app}\*.json`, `{app}\runtimes`, `{app}\EditorResources`, `{app}\Player`, `{app}\slang`.
  (`DefaultDirName` is versioned and `UsePreviousAppDir=no`, so this only hits same-version reinstalls.)
- `[Icons]` / `[Run]` keep pointing at `{app}\TiXL.exe`. No `WorkingDir` needed.

### 4. Keep `.pdb`s

Not removing them: they give line numbers in Sentry stack traces, and inside `app/` they no longer clutter
anything.

### 5. Docs

- `.help/docs/install/InstallLinux.md`: "The license text is in the `licenses` folder" stays true (now at the
  top). Launch and app-menu instructions (`~/Apps/tixl-*-linux-x64/TiXL`, `$PWD/TiXL`, `share/`) stay valid.
  Add one sentence that the program files live in `app/` and `TiXL` starts them.
- `.help/docs/install/Installation.md`: the build-from-source paths (`Editor\bin\...\net10.0-windows`) are dev
  layout and stay valid.

### 6. Manual test set

Add `.tests-manual/<area>/install-layout.md` (frontmatter `added: 2026-10-25`, `added-in-version: 4.4`) covering
the verification list below, so release testers run it once per installer change.

## Verification

Per platform (Windows installer, Linux tarball; AUR/Flatpak/Nix at least start-up):
1. Top-level folder contains only the entries from *Target layout*.
2. Editor starts from the launcher, also when started from another working directory and via a symlink (Linux).
   Log shows `StartFolder` = `.../app/`.
3. Create a new project, add an operator, edit its code → compiles against `app/Core.dll`.
4. Open an existing user project created with a flat install → still compiles.
5. Player export works and the exported player runs.
6. Settings → restart editor relaunches correctly.
7. Native libraries: audio (BASS), NDI, Ableton Link, OpenCV / MediaPipe ops, video (ffmpeg), Vulkan shader
   compile (slangc), MIDI.
8. Read-only install location (`Program Files`, `/usr/lib/tixl`) still works.
9. Windows only: Explorer and taskbar show the TiXL icon, no console window opens, file properties show the
   version, firewall prompt for OSC/NDI names `TiXL.exe`.
10. Windows: install over a flat install of the same version → old top-level files are gone; uninstall removes
    everything.

## Open points

- **Folder name.** `app/` is neutral and reads as "the program". Alternatives: `bin/` (Unix habit, but holds
  far more than executables), `TiXL/` (mirrors macOS, but `TiXL/TiXL` reads oddly). Changing it later means
  one property plus the installer paths.
- **Windows unverified.** The host code is shared with Linux and accepts `/` separators, but the icon/version
  resources and GUI subsystem flag on the generated launcher need one real run.
- **Portable Windows zip.** None exists today; if one is added, it should come from the same stage as the
  installer.
