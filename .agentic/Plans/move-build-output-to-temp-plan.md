# Move Build Output (bin/, obj/) into .temp/ — Plan

**Implemented 2026-08-26** as chain step `Editor/Migrations/Steps/To3_BuildOutputToTemp.cs`
(project format V3). Key learnings vs. the original draft below:

- The SDK only auto-excludes the *configured* output paths from the compile glob, so stale
  root-level `bin/obj` from format V2 entered the build as duplicate sources. Both generated
  props files therefore append `bin/**;obj/**` to `DefaultItemExcludes`, and the step deletes
  the stale folders (tolerantly — the running editor's shadow copies leave them unlocked).
- Migration now runs *before* the startup compile (inside the parallel project-load pass) —
  otherwise the first compile targets root bin/ and the step deletes fresh output.
- Repo: `Operators/Directory.Build.props` (chains up to the root props it shadows) covers all
  built-in packages; their `ClearBuildOutput`/`_StaleBuildOutput` targets and Editor.csproj's
  `CopyTixlOpProject` now use `$(BaseOutputPath)`/`.temp/bin`. Editor/Core keep their normal bin.
- Backups and share exports include the root `Directory.Build.props`, so unpacked projects
  build correctly without editor intervention.

Original plan for reference:

Drafted 2026-08-26, follow-up to the Symbols-folder restructuring. Goal: a project root that
contains *only* the canonical content — `<name>.csproj`, `Symbols/`, `Assets/`, `dependencies/`,
`.meta/` — plus a single `.temp/` for all transient state (backups today; build output after this).

Everything here assumes the Symbols-folder migration has shipped and settled. Do not start this
while that change is still being smoke-tested.

---

## 1. Target layout & mechanism

```
MyProject/
  MyProject.csproj
  Directory.Build.props        <- generated, sets the output paths (see below)
  Symbols/  Assets/  dependencies/  .meta/
  .temp/
    bin/Debug/... bin/Release/...
    obj/
    Backup/
```

Keep the `bin`/`obj` names under `.temp/` so tooling and people recognize them.

**Why a `Directory.Build.props` and not csproj properties:** `BaseIntermediateOutputPath` and
`MSBuildProjectExtensionsPath` are consumed by `Microsoft.Common.props`, which the SDK imports
*before* the csproj body is evaluated. Setting them in the csproj body leaves NuGet's restore
artifacts (`project.assets.json`, `*.nuget.g.props`) in a classic `obj/` — you end up with BOTH
`obj/` and `.temp/obj/`. `Directory.Build.props` is imported early enough. Properties to set:

```xml
<Project>
  <PropertyGroup>
    <BaseOutputPath>.temp/bin/</BaseOutputPath>
    <BaseIntermediateOutputPath>.temp/obj/</BaseIntermediateOutputPath>
    <MSBuildProjectExtensionsPath>.temp/obj/</MSBuildProjectExtensionsPath>
  </PropertyGroup>
</Project>
```

The file is editor-generated (creation + migration) and becomes part of the project's root files:
the allowlists (share export, backup, import) must include root `Directory.Build.props` next to
`*.csproj`. Alternatively treat it as derived state and have import/creation regenerate it —
decide during implementation; shipping it is simpler and keeps hand-unzipped projects working.

**Repo built-in packages:** their csprojs import `Tixl.props` explicitly in the body — too late
for `MSBuildProjectExtensionsPath` (same reason as above). Use a `Directory.Build.props` at
`Operators/` level instead (auto-imported early). Decide whether the rest of the solution
(Editor, Core, ...) also moves; nothing requires it — scope can stay Operators-only.

## 2. Code touch points (from the folder-filter audit)

- `CsProjectFile`: `_releaseRootDirectory` / `_debugRootDirectory` are hardcoded
  `<dir>/bin/<Config>`; `GetBuildTargetDirectory()` feeds `AssemblyInformation.Initialize`,
  player export, and the needs-recompile check. Centralize the base path ("where is build
  output") in one place instead of re-deriving.
- `ProjectXml.AddCleanBuildTarget`: `RemoveDir bin/$(Configuration)` →
  `$(BaseOutputPath)$(Configuration)`. Same for Lib's hand-maintained `ClearBuildOutput`
  (FFmpeg-preserving variant) and any other checked-in targets referencing `bin/`.
- `EditableSymbolProject.IsGeneratedCodeFile` (code watcher): add a `.temp` prefix skip
  (generated .cs then live under `.temp/obj/`). Keep `bin`/`obj` for legacy layouts.
- `TixlAssemblyLoadContext` directory-name skips (`bin`, `obj`, `Symbols`): still match by name
  under `.temp/`; verify the probing that *finds* the output assembly uses the csproj-derived
  path, not a hardcoded `bin`.
- `AutoBackup`: restore cleanup `TryDeleteRecursively(bin|obj)` → also/instead the `.temp`
  locations (but never `.temp/Backup`!). `ProjectLayout.GeneratedStateDirectories` keeps
  `bin`/`obj` entries for legacy sweeps.
- `CouldNotLoadProjectDialog` bin/obj skips: keep (legacy), consider adding `.temp`.
- `PlayerExporter`: consumes `GetBuildTargetDirectory` — should follow automatically; verify
  `IsNestedExportFile` and the copy excludes still hold.
- `.gitignore` (repo): ensure `.temp/` is ignored where `bin/`/`obj/` were.

## 3. Migration (user projects)

New chain step `Editor/Migrations/Steps/To3_BuildOutputToTemp.cs` (add `V3` to the `ProjectFormat`
enum and bump `FormatHelper.Current`); the `ProjectFormatMigration` runner applies it in order:

1. Write `Directory.Build.props` (skip if user already has one — then merge or warn, decide).
2. Delete stale root `bin/` and `obj/` — with tolerance: a running editor may hold loaded
   assemblies (shadow copies should make this safe; verify). If deletion fails, log and leave;
   they're inert once the props file exists.
3. Stamp version, save csproj.

No backup needed: only regenerable state is touched. First load after migration triggers one
full recompile per project (no assembly at the new location) — log it so slow startup is
explicable.

## 4. Risks

- MSBuild evaluation-order subtleties (the whole reason for the props file); test `dotnet build`,
  Rider, and VS against a migrated project — IDE up-to-date checks and NuGet restore must all
  agree on `.temp/obj/`.
- A user-authored `Directory.Build.props` in a *parent* folder of the projects directory would
  previously have been picked up; our per-project file shadows it (MSBuild stops at the first
  one found). Behavior change for anyone relying on that — obscure, but note in release notes.
- Locked build output during migration on a running editor (see §3).
- External tools/scripts pointing at `<project>/bin/...` (e.g. users' own launch shortcuts).

## 5. Acceptance

- New project: builds, hot-reloads, player-exports; root shows no `bin`/`obj`; NuGet artifacts
  land in `.temp/obj/` (no stray root `obj/`).
- Migrated legacy project: same, stale `bin`/`obj` gone (or logged as locked).
- Repo: full solution builds in Rider and `dotnet build`; Release package output layout inside
  `.temp/bin/Release/` unchanged (Symbols/SymbolUis/SourceCode/Assets); player export works.
- Backups and share packages contain no build output (already guaranteed by the allowlists).
- Manual test set added (extend `symbols-folder-migration.md` pattern).

## 6. Effort

Roughly 2–4 days including IDE verification. The MSBuild property mechanics are quick; the time
is in verifying Rider/VS/dotnet agreement and the running-editor migration edge cases.
