# Plan: Make the build-output clean step incremental

Status: analysis + proposal (2026-08-23). No code changed yet.

## Symptom

Every `dotnet build` (CLI, Rider, editor hot-reload, player export) first wipes `bin/<Configuration>`
and then recreates it. Nothing-changed builds therefore still do thousands of file operations.

## Measurements (this machine, NTFS on NVMe, no editor running, `--no-restore`, nothing changed)

| Build                              | Wall    | Of which wipe + recopy                                   |
|------------------------------------|---------|----------------------------------------------------------|
| `Operators/Lib` Debug              | 2.9 s   | ~0.15 s (Delete 114 files, Copy 116 MB)                  |
| `Editor` Debug                     | 3.5 s   | ~1.1 s (RemoveDir 1320 files/626 MB, Copy 0.85 s)        |
| `t3.sln` Debug                     | 7.9 s   | ~3.5 s (12 clean targets, RemoveDir 0.7 s, Copy 2.5 s)   |
| `Editor` Release                   | 28 s    | **~25 s** (`CopyTixlOperators`: 1060 Copy calls, 7500 hardlinks) |
| `t3.sln` Release                   | 39 s    | **~35 s** (Copy 30.5 s, RemoveDir 3.3 s, Delete 1.4 s, `CopyPackageContent` 4.4 s) |

So Debug is merely wasteful; Release is dominated by the clean (≈90 % of a no-op build). Player export
compiles every package in Release (`CsProjectFile.TryCompileRelease`) and pays the same price per package.
Machines with slower disks, OneDrive, or real-time AV scanning of every freshly written DLL pay a multiple of
these numbers — a freshly copied file is a "new" file to scan, a hardlink or a skipped copy is not.

Output sizes that get wiped and recreated: `Editor/bin/Debug` 626 MB (363 MB of it the published Player
copy, 101 MB `Dependencies/`, 109 MB NuGet `runtimes/`), `Editor/bin/Release` 2 GB / 7679 files,
`Operators/Video/bin/Debug` 336 MB, `Lib` 116 MB, `Io` 109 MB.

## Why the clean exists (history, from git)

1. Until June 2025 each package built into a **per-version subfolder** (`bin/Debug/<version>/<tfm>`) so a
   new version could be loaded while the old one was still mapped (unload timing is not guaranteed).
2. `0d39a93cc` "simplify build process in favor of runtime dll copies" removed the version subfolders and
   introduced the **shadow copy** in `TixlAssemblyLoadContext.LoadAssembly` (editable packages are copied
   to `%AppData%/…/Tmp/ShadowCopy/<pid>/…` and loaded from there, so `bin/` can be overwritten while loaded).
3. One day later `352289558` added `ClearBuildOutput` (`RemoveDir bin/$(Configuration)`) to Editor, Player
   and every operator csproj, and `ProjectXml.AddCleanBuildTarget` makes the editor **auto-inject** it into
   any operator csproj it opens (`CsProjectFile.CsProjectLoadInfo`), so it self-propagates to user projects.

The reason it was needed: the loader treats the package output directory as the truth of what belongs to
the package (`AssemblyTreeNode` scans sibling DLLs, `TryFindUnreferenced`, NuGet fallback, `OperatorPackage.json`).
MSBuild's own `IncrementalClean` only removes files it wrote in a previous build **and recorded in
`obj/…/*.FileListAbsolute.txt`**. The custom `<Copy>` targets (`CopyPackageContent`, `CopyTixlOpProject`,
`CopyResources`, `CopyDefaults`, `CopyEmbeddedHelp`, `CopyPlayer`) never register `FileWrites`, and they run
`AfterTargets="AfterBuild"`, i.e. after `IncrementalClean` already ran. So stale files (renamed assemblies,
removed NuGet packages, leftover version subfolders, removed `.t3`/assets) accumulated and were picked up —
"issues with custom UIs and whatnot". The blunt wipe fixed that, at the price above.

Two side effects of the wipe, both observed today:
- Building Editor in the configuration of a **running** editor deletes every file that is not locked
  (Operators/, Player/, .help/, .tixl/, unloaded DLLs) and then fails with MSB3231 on the first locked DLL,
  leaving the running instance gutted. `Lib.csproj` already had to special-case FFmpeg natives for this.
- A wipe that fails halfway (locked file, AV, sync client) leaves exactly the "inconsistent build folder"
  that the wipe was meant to prevent; the next build then wipes again and usually succeeds — which is
  why it *feels* like "it works, just slowly".

## What the shadow copy adds at startup (out of scope here, but related)

`LoadAssembly` copies all `*.dll/*.exe/*.pdb/*.xml/*.json` of each editable package recursively
(incl. `runtimes/**` natives: Lib 116 MB, Video 336 MB, Io 109 MB ≈ 0.6 GB) on every editor start. Natives
are resolved with `DllImportSearchPath.AssemblyDirectory` (→ shadow copy) with a `MainDirectory` fallback,
so they are only partially needed there. A later slice could skip `runtimes/**` and non-managed files
in the shadow copy; not part of this plan.

## Proposal

Keep "the output folder contains exactly what this build produces" as the invariant, but achieve it
incrementally. Three pieces, in order of payoff/risk:

### 1. Replace wipe+copy with mirror semantics in the custom copy targets (biggest win, Release −25–30 s)

For each custom copy target, compute the expected destination set from the source items, delete only
destination files **not** in that set, and `Copy` with `SkipUnchangedFiles="true"` (timestamp+size; a
hardlinked file is always "unchanged"). Plain MSBuild items, no history needed:

```xml
<ItemGroup>
  <_Expected Include="@(ProjectFiles->'$(Dest)/%(RecursiveDir)%(Filename)%(Extension)')" />
  <_Existing Include="$(Dest)/**" />
  <_Stale Include="@(_Existing)" Exclude="@(_Expected)" />
</ItemGroup>
<Delete Files="@(_Stale)" />
<Copy SourceFiles="@(ProjectFiles)" DestinationFiles="@(_Expected)" SkipUnchangedFiles="true" UseHardlinksIfPossible="true" />
```

Applies to `CopyTixlOpProject` (the 25 s), `CopyPackageContent` (4.4 s), `CopyResources`, `CopyDefaults`,
`CopyEmbeddedHelp`, `CopyPlayer`. This is robocopy `/MIR` expressed in MSBuild: deterministic,
independent of `obj/` state, and it no longer needs the wipe to remove stale *copied* files.

### 2. Drop the blunt `RemoveDir`; rely on `IncrementalClean` for the standard output, with a stamp-guarded full clean as the safety net

The standard outputs (main assembly, copy-local references, `Content`, NuGet `runtimes/`) are already
tracked by `FileWrites`, so `IncrementalClean` removes them when they disappear from the build — as long
as `obj/…/FileListAbsolute.txt` matches the folder. The cases where that is *not* trustworthy are
enumerable, so make the full wipe **conditional** instead of removing it:

- a stamp file `bin/<cfg>/<tfm>/.tixl-build-stamp` written at the end of a successful build, containing
  a hash of: csproj text (references/packages changed ⇒ stale DLL risk), `Tixl.props`, TFM, SDK version,
  and a schema number we can bump when the layout changes;
- `ClearBuildOutput` runs only when the stamp is missing, mismatched, or `-p:TixlCleanOutput=true` is
  passed. Missing stamp also covers "someone deleted obj/ but not bin/" (stamp lives in bin, `obj/`
  history lives in obj; either half missing ⇒ wipe);
- `dotnet clean` / Rider "Rebuild" keep working as the explicit escape hatch.

This gives: no-op build → no wipe; csproj edited (add/remove package) → full wipe once; version bump
(`VersionPrefix` changes the csproj text) → full wipe once (acceptable, matches today's `NeedsRecompile`).

### 3. Let the editor request the clean when it has evidence (the "infer from failures" part)

`Compiler.TryCompile` builds the command line; add an optional `-p:TixlCleanOutput=true` that the
editor passes when:
- the previous compile of this project failed (`EditableSymbolProject.Recompilation` knows),
- the previous load of the built assembly failed (`AssemblyInformation`/`TixlAssemblyLoadContext` errors
  — "Failed to load root assembly", type-conflict warnings),
- `CsProjectLoadInfo.NeedsRecompile` was set because the csproj was migrated/repaired,
- the user explicitly chooses "Rebuild project" (new menu item / existing force-recompile path),
- and on the first startup after an editor version change (one-shot).

`ProjectXml.AddCleanBuildTarget` must emit the *conditional* target and `CsProjectLoadInfo` must
upgrade existing unconditional ones (same migration pattern as `CreatePackageInfo` today), otherwise
user projects keep the old behaviour forever.

Also worth doing while there: `Compiler.TryCompile` does not pass `--no-dependencies` (the unused
`GetCommandFor` does). The runtime build of a built-in package therefore walks Core/IoServices/Serialization/
SystemUi/Logging/Mediapipe every time (`ResolveProjectReferences` ≈ 2.7 s of Lib's 2.9 s no-op build).
Since the editor can't hot-reload Core anyway, compiling against the on-disk Core.dll it already loaded is
both faster and more consistent. Needs a check that a fresh clone still builds Core first (startup does
`dotnet restore` + build; the editor itself only runs once Core is built, so yes).

## Risks / things to verify

- `SkipUnchangedFiles` compares size + last-write time; hardlinks share both, real copies preserve the
  source time (MSBuild `Copy` uses `File.Copy`, which preserves LastWriteTime on Windows) — verify once
  with a deliberately touched source.
- `_Existing Include="$(Dest)/**"` enumeration cost on `Editor/bin/Release/Operators` (7.5 k files) —
  should be ≪ 1 s; measure.
- Read-only built-in packages in a Release editor are loaded *without* shadow copy and are hardlinked to
  `Operators/<pkg>/bin/Release`. A Release build of that package while the Release editor runs can't
  replace the locked file either way; the mirror approach at least leaves the folder intact instead of
  half-deleted.
- Anything that intentionally relied on the wipe to remove runtime-written files from `bin/` (none found:
  ImGui ini is disabled, settings live in `%AppData%`, Defaults write to repo `.Defaults` in Debug).
- Keep `Lib.csproj`'s FFmpeg exclusion semantics: with the conditional wipe the normal path never deletes
  the natives; the forced path should still `ContinueOnError` on locked files and then mirror.

## Suggested order

1. Mirror copies in `Editor.csproj` (`CopyTixlOpProject` + the five small ones) — measurable on its own,
   Release build should drop from ~28 s to a few seconds.
2. Mirror `CopyPackageContent` in the operator csprojs + generator.
3. Stamp-guarded `ClearBuildOutput` in `ProjectXml.AddCleanBuildTarget` + migration of existing targets +
   Editor/Player csproj.
4. Editor-side evidence → `-p:TixlCleanOutput=true`; `--no-dependencies` in `Compiler.TryCompile`.
5. Manual test set: fresh clone, csproj reference removed, obj deleted, bin deleted, editor-running build
   in the other configuration, player export.
