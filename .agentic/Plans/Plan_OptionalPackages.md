# Plan: Optional operator packages (separate, discoverable, installable from the editor)

**Status:** Proposal 2026-11-23 (`main` d6bce9b43). Nothing implemented. Decisions marked *proposed* need a yes.
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

## Proposed decisions

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

### Phase 0: Move and gate (part of v4.4 milestone M0)

- [ ] `Operators/AbletonLink/`: move `AbletonLinkSync.{cs,t3,t3ui}` with `git mv` (GUIDs unchanged, namespace
      kept), its three files from `Dependencies/` into `dependencies/`. Follow commit 5f945a2c8 (Video left Lib).
- [ ] Mediapipe: move Emgu and `mediapipe_c.dll` into `Operators/Mediapipe/dependencies/`. Find out whether Lib
      and Spout really use `CppSharp.Runtime`, `Google.Protobuf`, `OpenGL.Net`; drop the references if not, so
      they stop shipping with the default install.
- [ ] Owner + licence for every remaining `Dependencies/` file (DanceAi, ArtNet, libusb, …).
- [ ] D2 metadata on every package; `PackageNames` → glob + filter; licence check script in all three CI
      workflows.
- Verify: release artifacts on all platforms contain no Link/Emgu/Mediapipe files; Debug editor still loads both
  packages; a project using `[AbletonLinkSync]` opened in a Release build keeps the op through a save.

### Phase 1: Pack and manual install (needed for 4.4, see open decision 5)

- [ ] `pack` target per package → `TiXL.<Name>.<ver>.nupkg` from the Release folder; CI attaches the optional
      packages to each GitHub release.
- [ ] Load installed packages from `<SettingsDirectory>/Packages` at startup (D6).
- [ ] Drag & drop / file picker for a `.nupkg`: validate packageType, `EditorVersion`, platform → install →
      restart prompt.
- Verify: fresh Windows install without Mediapipe, install the nupkg from the release page, Mediapipe examples run.

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

## Open decisions

1. **Hosting:** nuget.org (recommended, D4) or GitHub Releases only?
2. **Precompiled vs source packages:** precompiled recommended (D3); source would avoid per-release repacking but
   needs restore + compile on the user's machine.
3. **Community packages in the catalog from the start**, or official only until Phase 4?
4. **Restart for update/uninstall** acceptable (D7)?
5. **Sequencing gap:** v4.4 removes Mediapipe from *all* release builds, including Windows, where it ships today.
   Without Phase 1 in 4.4, Windows users who rely on Mediapipe have no supported way to get it back. Recommended:
   Phase 1 joins the 4.4 scope (it is small); alternative: keep Mediapipe bundled on Windows for one more release.
