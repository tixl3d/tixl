# Plan: Optional operator packages (separate, discoverable, installable from the editor)

**Status:** Decisions taken 2026-11-23 (`main` d6bce9b43), except hosting (D4, recommendation stands). Nothing implemented.
**Builds on:** [Plan_Release_v4.4](Plan_Release_v4.4.md) §1 (Link and Emgu leave the default install, decided
2026-11-20) and the feed sketch in [share-project-and-packages-plan](share-project-and-packages-plan.md) §6.
**First candidates:** Ableton Link (GPLv2+, Windows-only native today) and Mediapipe (Emgu GPLv3, ~87 MB of
models, Windows-only natives).

## Goals

1. Release builds only contain what may ship there, checked by CI and decided by metadata on the package
   rather than by a list someone has to remember.
2. Users find and install optional packages inside the editor; installing needs no restart.
3. A contributor can turn a set of operators into an optional package by following one checklist, with an AI agent
   doing most of it.

## What already exists (checked in code)

| Piece | Where | Consequence |
|---|---|---|
| Release package folder = precompiled DLL + natives + `OperatorPackage.json` + `Assets/` `Symbols/` `SymbolUis/` `SourceCode/` | `Copy*PackageContent` targets in each package csproj | This folder *is* the install format. No new layout needed |
| Any such folder under `<app>/Operators/` loads read-only | `ProjectSetup.LoadBuiltInPackages` (`Editor/Compilation/ProjectSetup.Startup.cs:206`) | Loading from a second directory is a few lines |
| Loading a package while the editor runs | `ProjectSetup.SetProjectArchived(…, false)` (`Editor/Compilation/ProjectSetup.cs`): `AddToLoadedPackages` → `GenerateLoadContext` → `UpdateSymbolPackages` → `InitializePackageResources` | Live install has a working template |
| Missing operators survive load and save | `Symbol.UnresolvedChildren` (`Core/Operator/Symbol.UnresolvedChildren.cs`) | Opening a project without the package is safe. **But** nothing revives them at runtime; they only return on the next load |
| `OperatorPackage.json` records `EditorVersion` | `CreatePackageInfo` target | Compatibility check needs no new field |
| `.nupkg` writer (content-only, packageType `TixlProject`) | `Editor/UiModel/Exporting/ProjectPackageExporter.cs` | Zip/nuspec/OPC code to reuse |
| Bundled packages = hard-coded `PackageNames` list | `Editor/Editor.csproj:198` | Replace with metadata (Phase 0) |
| All of `Dependencies/**` copied into every build | `Editor/Editor.csproj:102`, `Player/Player.csproj:36` | Package natives must move out of it |

Gaps found while checking:

- `Dependencies/` root also holds Mediapipe's `mediapipe_c.dll`, plus `CppSharp.Runtime.dll`,
  `Google.Protobuf.dll`, `OpenGL.Net.dll` (also NuGet references of Lib and Spout, not only Mediapipe),
  `DanceAi.dll`, `dance-phase/`, `ArtNet.dll`, `libusb-1.0.dll`. The v4.4 licence table only lists Link, Emgu,
  NDI and DirectShowLib. Every file there needs an owner and a licence before the move.
- A missing operator's name does not tell you its package: Mediapipe symbols live in namespace
  `Lib.io.video.mediapipe`, and `AbletonLinkSync` keeps its `Lib` path after moving (GUIDs stay). Discovery
  therefore needs a symbol-GUID → package map.

## Decisions

### D1. Repository: monorepo by default, separate repos only for third parties

An official optional package is a folder `Operators/<Name>/` in this repo, exactly like Ndi, Spout, Video today.

- Precompiled packages bind to `Core`'s API. Building them in the same commit and CI run as the editor keeps them
  in lockstep for free; a separate repo would need a published SDK package and a rebuild on every TiXL release.
- Debug builds already load every `Operators/*` as an editable project, so developers lose nothing by moving ops
  out of Lib.
- **No submodules.** They add a clone step every contributor gets wrong and solve nothing a folder doesn't.
- Separate repositories are for third-party packages (Phase 4), built against a `TiXL.Sdk` reference package.

### D2. Metadata in the package csproj, one place for contributors

```xml
<PropertyGroup>
  <!-- Bundled: in release builds. Optional: published, installed on demand. DevOnly: never shipped. -->
  <TixlDistribution>Optional</TixlDistribution>
  <!-- Platforms the package works on; release packing and the Packages window filter by it. -->
  <TixlPlatforms>win-x64</TixlPlatforms>
  <!-- The strictest licence of the package code, its NuGet references and its natives. -->
  <PackageLicenseExpression>GPL-2.0-or-later</PackageLicenseExpression>
  <Description>Sync TiXL's beat time with Ableton Live and other Link apps.</Description>
</PropertyGroup>
```

- Licence and platform are separate axes: Spout is `Bundled` + `win-x64`; Ableton Link is `Optional` + (for now)
  `win-x64`.
- `Editor.csproj` replaces the `PackageNames` list with a glob over `Operators/*/*.csproj`, filtered by
  `TixlDistribution == Bundled` and the build's RID.
- **The licence gate is a CI check, not a convention.** A script fails the build if a `Bundled` package declares
  a licence outside the allowlist (MIT, MIT-0, BSD-2/3-Clause, Apache-2.0, Apache-2.0 WITH LLVM-exception, Zlib,
  LGPL for dynamically linked natives) or if a release artifact contains a native file not owned by a bundled
  package. That is the "MIT build" tag, and it is the same rule on every platform (v4.4 already decided that
  Mediapipe and Link are in no release build).
- Each package keeps the licence texts of what it ships in `dependencies/licenses/`; packing copies them along.

### D3. Artifact: precompiled `.nupkg`, one per package × TiXL minor version

Contents = the release folder from the table above, natives under `runtimes/<rid>/native/`, `SourceCode/` kept
for reading as with built-in packages. packageType `TixlOperatorPackage`.

- **Precompiled, not source:** installing needs no `dotnet restore` (Mediapipe pulls OpenCvSharp, Protobuf, …),
  no network to NuGet sources the user may not reach, no compile that can fail on the user's SDK. The loader
  already handles exactly this format for built-in packages.
- **All platforms in one package** (decided 2026-11-23: precompiled is fine as long as it works on every platform).
  The operator DLL targets `net10.0` and is the same everywhere; only natives differ. One `.nupkg` per package
  carries `runtimes/{win-x64,linux-x64,osx-arm64}/native/` for every RID in `TixlPlatforms`. Packing runs once,
  after per-platform native builds have uploaded their artifacts (the `native-audio.yml` pattern). The loader
  copies or resolves only the host RID's natives. A package whose `TixlPlatforms` lacks the host RID is shown
  as "not available on this platform", never installed.
- **Compatibility:** `OperatorPackage.json`'s `EditorVersion` must match the editor's major.minor. CI repacks
  every official package for each TiXL release (cheap in the monorepo). A mismatched package is not loaded; its
  operators stay unresolved (lossless) and the Packages window offers the matching version.

### D4. Hosting: nuget.org, accessed with plain HTTP

- IDs `TiXL.<Name>`; reserve the `TiXL.` prefix on nuget.org so only official packages can use it.
- The editor uses two endpoints and no NuGet client library: search
  (`…/query?q=&packageType=TixlOperatorPackage`) and the flat-container download
  (`v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg`). Versions on nuget.org are immutable, which gives
  reproducible installs.
- 250 MB package limit: Mediapipe (~87 MB) fits. Bigger model sets would need a post-install download with hash
  check; no current candidate needs that.
- Rejected: GitHub Packages (needs auth even for public feeds); GitHub Releases + static index (fine for
  official packages, but no story for third parties, and we'd write the index format ourselves).

### D5. Discovery and trust: a curated catalog in this repo

`packages/catalog.json` (fetched from `main` on GitHub, cached; the release ships a copy as offline fallback):

```json
{ "id": "TiXL.AbletonLink", "title": "Ableton Link", "tier": "official",
  "licence": "GPL-2.0-or-later", "platforms": ["win-x64"],
  "symbols": ["<guid>", "…"] }
```

- nuget.org stores the files and their versions; the catalog decides what is listed and how trusted it is.
  Installing a package means running its code with the user's rights, so listing it goes through a reviewed PR.
  That PR is also the contributor process: transparent, and an agent can prepare it.
- `symbols` is generated by CI from the package's `.t3` files. It powers the most useful entry point:
  *"This project uses [AbletonLinkSync] from Ableton Link (GPL, Windows) — Install"*, shown wherever unresolved
  children are reported today.
- A local `.nupkg` can still be installed by drag & drop, with an "unlisted package" warning.

### D6. Install location and load order

- `<SettingsDirectory>/Packages/<Id>/<Version>/`. The app folder may be read-only (Program Files, macOS
  translocation, Flatpak), and the settings folder is already the writable place.
- `<SettingsDirectory>/Packages/installed.json` lists what the user chose; startup loads `<app>/Operators/*`
  first, then the chosen version of each installed package; an installed package with the id of a bundled one
  is ignored with a warning.

### D7. Live install yes; update and uninstall need a restart

- **Install:** download → extract to a temp folder → move into place → load like `SetProjectArchived` does →
  **new:** `Symbol.TryResolveUnresolvedChildren()` for every symbol whose unresolved `SymbolId` is now
  registered (rebuild the child from its kept JSON, move its connections back, re-create instances of the
  parent). The new step is the only real engineering in the whole plan and should live in `Core` beside
  `UnresolvedChildren`. Its round-trip test: open a project without the package, install, compare instance
  graph and saved files against opening with the package present.
- **Update / uninstall:** both replace or drop a loaded `AssemblyLoadContext` that has live instances, and
  unloading cascades into other contexts (see the comment in `ProjectSetup.UpdateSymbolPackagesInternal`).
  Record the change as pending in `installed.json` and apply it at the next start, before loading. Honest and
  cheap; revisit only if users ask.

### D8. Player export

`PlayerExporter` collects packages from both directories and copies the natives for the target RID, plus
the package's `licenses/`. An export that contains a GPL package must say so in the export dialog.

## Phases

### Phase 0: Move and gate (part of v4.4 milestone M0) — done 2026-11-23, uncommitted

- [x] `Operators/AbletonLink/`: `AbletonLinkSync` moved (GUIDs and `Lib.numbers.anim.time` namespace kept) with its
      native `AbletonLinkDLL.dll` and the GPL-2.0 text. The managed `AbletonLink.dll` + `.deps.json` in
      `Dependencies/` were an unused 2024 wrapper and are deleted.
- [x] Mediapipe: `mediapipe_c.dll` + Apache-2.0 text moved into its `dependencies/`. **Emgu CV (`Emgu.CV.dll`,
      `cvextern.dll`, GPLv3 text) deleted**: no code used it and `mediapipe_c.dll` doesn't link it.
- [x] Ndi: NDI runtime + current licence text moved into `Operators/Ndi/dependencies/`.
- [x] `Dependencies/` copies of NuGet assemblies deleted (ArtNet, CppSharp.Runtime, Google.Protobuf, OpenGL.Net,
      NDILibDotNet6, Unsplasharp); Ndi, Unsplash and Spout now carry exactly those via `TixlCopyLocalPackages`
      (root `Directory.Build.targets`), not the 140 MB that `CopyLocalLockFileAssemblies` would add. Also deleted:
      `DirectShowLib.dll` (unused), the Kinect for Windows SDK 2.0 beta EULA (unused).
- [x] What stays in `Dependencies/` is listed with owner and licence in `Dependencies/third-party.json`
      (DanceAi + model, libusb, tixlaudio). LGPL-2.1 text added for libusb, BSD-2-Clause text for Spout.
- [x] D2 metadata on all 11 packages. `Editor.csproj` ships packages with `TixlDistribution=Bundled` whose
      `TixlPlatforms` include the build's platform (`TixlBuildPlatform`: `TixlEditorRuntime` or host OS).
      Checked with the copy target alone: Linux/macOS ship Examples, Io, Lib, Skills, TypeOperators, Unsplash,
      Video; Windows adds Spout; AbletonLink, Mediapipe, Ndi ship nowhere.
- [x] `Installer/check-licences.py` (Editor + bundled packages: own expression, every restored NuGet package's
      nuspec licence, `Dependencies/` inventory) in `linux-build`, `macos-build`, `windows-build` and `pr.yml`.
      Passes; a deliberately bundled Ndi and a stray DLL are both reported.
- [x] Startup log: missing operators in read-only (shipped) packages go to the debug log, like the dialog.
- Verified: full solution build without warnings; Debug editor loads all 15 packages incl. AbletonLink with no
  missing operators; `UnresolvedChildren_SurviveLoadAndSave` passes.

Follow-ups found during Phase 0:
- **AbletonLinkDLL.dll is a GPL binary of unknown source.** Before the package is published (Phase 1), find the
  source of that wrapper and ship a link to it (GPL), or rebuild it from Ableton's Link repo in CI (also gives
  Linux/macOS natives).
- Replace `NDILibDotNet6` (no licence) with own MIT P/Invoke bindings.
- `CopyLocalLockFileAssemblies` in Lib, Io, Video, Mediapipe copies the editor's own dependencies into each package
  (Debug outputs 881/218/397/241 MB; OnnxRuntime four times). `TixlCopyLocalPackages` can replace it; matters for the
  Mediapipe `.nupkg` (nuget.org limit 250 MB) and installer size.
- Windows-only natives still in `Dependencies/` root (tixlaudio.dll, libusb) reach Linux/macOS builds as dead weight;
  `Dependencies/win-x64/` like `linux-x64/`.
- `licenses/BSD.txt` covers AvalonDock/AvalonEdit (Tooll 2, gone); generate THIRD-PARTY-NOTICES from
  `third-party.json` plus the NuGet data the licence check already reads.
- `PackageLicenseExpression` `LicenseRef-NDI-SDK` is not accepted by nuget.org; the Ndi `.nupkg` needs
  `PackageLicenseFile` instead.

### Phase 1: Pack, Extensions page, install on restart (in 4.4 scope, decided 2026-11-23)

Decided 2026-11-23 (later the same day): installing must be as easy as *Settings → Extensions → tick a checkbox →
Install & Restart*, so all optional packages (Ndi included) install the same way. The list comes from the
`.nupkg`s attached to the GitHub release of the running version (no catalog, no nuget.org yet); the download is
unpacked into `<SettingsDirectory>/Extensions/` and applied on the restart, so live loading (Phase 2) isn't needed.

Progress 2026-11-23 (uncommitted):
- [x] `Installer/pack-extensions.py`: `TiXL.<Name>.<TiXL version>.nupkg` per Optional package (package folder under
      `tixl/`, nuspec with packageType `TixlOperatorPackage`, platforms as tags, licence file for `LicenseRef-`) and
      `extensions.json` (title, description, licence, platforms, size, SHA-256). NuGet restore accepts the files.
      `windows-build.yml` packs on every run, uploads an artifact and attaches to the release on tags.
- [x] Mediapipe uses `TixlCopyLocalPackages` (OpenCvSharp4, its Windows runtime, Google.Protobuf) instead of copying
      the editor's dependencies: 107 MB instead of 171 MB. The OpenCV FFmpeg plugin is dropped by wildcard (Lib's
      drop target still named 4.11 and has shipped the 4.13 plugin since the update; fixed).
- [x] Editor `ExtensionPackages/`: `InstalledExtensions` (stage in `<settings>/Extensions/.pending`, `pending.json`,
      applied in `ProjectSetup.LoadAll` before loading; installed ones load after built-ins and projects, skipped if
      the same PackageId is loaded or built for another TiXL minor) and `ExtensionCatalog` (index from
      `github.com/tixl3d/tixl/releases/download/v<version>/extensions.json`, `TIXL_EXTENSIONS_INDEX` overrides with a
      URL or folder; size + SHA-256 check; archive validated; Install from File checks platform tags).
- [x] Settings → Extensions page (compact rows, greyed when unavailable, Install & Restart / Restart to Apply,
      Install from File...).
- [x] Missing operators in read-only packages log at debug level (Core `SymbolJson` and the editor summary).
- Verified on Linux with a Release editor in an isolated settings folder: staging NDI through the catalog code,
      applying + loading it at startup (Examples' NDI operators resolve), staging and applying the removal, file
      install (Windows-only package refused, NDI staged), real URL without a published index reports it clearly.
      Not yet seen by a human: the page itself, the restart from the button, Windows.
- Docs: `.help/docs/using/Extensions.md`, release notes, `.tests-manual/extensions-install.md`.

### Phase 2: Live install

- [ ] `Symbol.TryResolveUnresolvedChildren` + round-trip test (D7).
- [ ] Install without restart; update/uninstall as pending changes.

### Phase 3: Catalog and Packages window

- [ ] `packages/catalog.json` + CI job that regenerates `symbols` and checks entries against the packages.
- [ ] Packages window: installed / available (filtered by platform), licence shown prominently, install button.
- [ ] "Install" action wherever unresolved operators are reported.
- [ ] CI publishes official packages to nuget.org on `v*` tags (API key in a GitHub environment).
- [ ] PlayerExporter (D8).

### Phase 4: Third-party packages in their own repos

- [ ] `TiXL.Sdk` on nuget.org: reference assemblies of Core, Graphics, Graphics.Compat, Logging plus the shared
      `Using` props that every package csproj repeats today.
- [ ] Template repository with a GitHub Action that builds, packs and publishes; catalog PR as the listing step.

## Contributor checklist (to become `.help/docs/dev/OptionalPackages.md` once Phase 0 is done)

1. Copy an existing package folder (`Operators/Ndi/`: three operators plus natives), rename csproj, `RootNamespace`, new `PackageId` GUID.
2. `git mv` the operators' `.cs/.t3/.t3ui`; never regenerate GUIDs.
3. Move natives into `dependencies/` (per RID where needed) and their licence texts into `dependencies/licenses/`.
4. Set `TixlDistribution`, `TixlPlatforms`, `PackageLicenseExpression`, `Description`.
5. Add the project to `t3.sln`; build Debug and Release; run the licence check.
6. Open a project that used the operators in a Release build without the package (op kept), then with it.
7. Add the catalog entry (Phase 3 onward).

## Decisions taken 2026-11-23

1. **Hosting:** undecided. Recommendation stays nuget.org (D4): free, immutable versions, works for community
   packages without us running anything. It only matters from Phase 3; Phase 1 attaches `.nupkg`s to GitHub
   releases either way, so nothing blocks on it.
2. **Precompiled packages**, with natives for every supported platform in the same package (D3).
3. **Community packages in the catalog from the start** (`tier: community`), reviewed by catalog PR.
4. **Restart for update and uninstall** is fine (D7).
5. **Phase 1 is part of 4.4**, so Windows Mediapipe users can install it from the release page.

## Decisions taken 2026-11-23 during Phase 0

6. **Ndi is optional on all platforms.** Its C# wrapper `NDILibDotNet6` declares no licence (no author, no
   repository) and the Windows runtime is proprietary, so with Ndi bundled no default build would be free
   software. Follow-up: own MIT P/Invoke bindings to the NDI C API replace the wrapper.
7. **MS-PL is allowed** for bundled packages (Svg, Svg.Custom in Lib; licence text already shipped).
8. **SixLabors.Fonts** (Six Labors Split License) counts as Apache-2.0: TiXL is open source and users get it
   transitively.
