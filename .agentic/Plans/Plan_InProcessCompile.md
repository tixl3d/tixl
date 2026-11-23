# Plan: Compile operator projects in-process (users need no .NET SDK)

**Status:** Phases 1 and 2 implemented 2026-11-23 on `main` (uncommitted, Linux only so far). Phase 3 open. Decided 2026-11-23: ships in 4.4 on all three platforms.

## Progress (2026-11-23)

- `OperatorCompiler/` library: `EvaluatedProject` (csproj subset, anything else becomes a "needs SDK" reason),
  `CompilerSupport` (reference pack + generators, cached, thread-safe), `InProcessCompiler`, `GeneratedSources`,
  `BuildOutputFiles`. `CompilerSupport.targets` downloads `Microsoft.NETCore.App.Ref` 10.0.12 and copies it to
  `<output>/CompilerSupport/`; Editor and tests import it.
- Editor: `Compiler.TryCompile` tries in-process first, falls back to `dotnet build` on "needs SDK" or an exception;
  log line ends with "(built-in compiler)". Setting `Compile Operators Inside TiXL` (Settings → Diagnostics →
  Compilation, default on). The startup SDK warning is skipped when `CompilerSupport` is present. A failed SDK
  fallback names the reason the project needs the SDK.
- `OperatorCompiler.Tests` (6 tests, all pass on Linux): shipped files alone compile a fixture; Release clears and
  copies package content; unsupported projects need the SDK; Debug and Release builds match `dotnet build`
  (metadata + IL, PDB options, generated sources, JSON files, output file list); errors match `dotnet build`.
- Your 9 compiling user projects: identical to `dotnet build` with the production library.
- Live editor (isolated HOME, Linux): 4 user projects compiled in-process at startup (1.5 s, parallel);
  hot reload via `reload` swapped the code (probe op v1 → v2); a project with a NuGet package fell back to the SDK.
- Manual tests: new `.tests-manual/compile-without-sdk.md`; `build-failure-messages.md` SDK steps now turn the
  setting off first. Release-notes entry under Installation.

Open: Windows and macOS runs, Player export (`TryCompileRelease`) in the live editor, Rider debugging of a user op,
CI running `OperatorCompiler.Tests`, Phase 3 (installers and install docs still list the SDK).
**Related:** [Plan_Release_v4.4](Plan_Release_v4.4.md) ("Embedding the .NET SDK" was out of scope; this removes the
need), [Plan_OptionalPackages](Plan_OptionalPackages.md) (packages are precompiled, so they never need this).

## Why

The editor runs on a bundled runtime. The SDK is needed for one thing: `Compiler` runs `dotnet restore` +
`dotnet build` on operator projects (`Editor/Compilation/Compiler.cs`). Every new or combined symbol is a C#
class, so without the SDK users can play projects but not build their own operators. Today Linux and macOS users
install the SDK themselves, the Windows installer runs Microsoft's SDK installer, and Nix/Flatpak need a writable
SDK with network at runtime.

Roslyn (`Microsoft.CodeAnalysis.CSharp` 5.6) already ships with the editor, and `Lib/Utils/CSharpSnippetCompiler`
already compiles C# in-process for `[CustomScalarField]` and friends.

## Spike result

The spike evaluates the csproj without MSBuild, compiles with the editor's Roslyn against the .NET reference
assemblies, runs the SDK's source generators, and writes dll, pdb, `OperatorPackage.json` and `deps.json`. Each
build was compared with `dotnet build -c Debug` (the editor's build mode for user projects) of the same sources:

- **Metadata + IL:** an order-independent dump of every type, member, signature, attribute and method body
  (IL with tokens resolved to names). Equal dumps mean equal behaviour.
- **PDB:** the compile options and references the compiler records (defines, nullable, optimization, language version).
- **Generated sources** (global usings, AssemblyInfo, TFM attribute) byte by byte, since they set the line
  numbers in error messages. `OperatorPackage.json` and `deps.json` as JSON.

| Set | Result |
|---|---|
| 10 user projects (`~/Documents/Tixl-pixtur`, 162 files, copies) | 9 identical in all four checks. ArtNet (old format, references `SharpDX*.dll`) fails in both builds with the **same 30 errors** (file, line, column, code) |
| 10 packages in `Operators/` (1,859 files, `git archive` copy) | Identical metadata, IL, PDB options and generated files once both use Roslyn 5.6 |
| Same packages, SDK on its own Roslyn (5.0 on Arch) | Lib and Mediapipe differ only by newer codegen (no `<>y__InlineArrayN` helpers, one temp local less when indexing spans); the same thing a normal SDK update does |

Not byte-identical DLLs: the PDB records the compiler version string, which changes the deterministic content
hash. Everything that affects behaviour or debugging matches.

Things the spike had to get right, each found by a mismatch: SDK reference pack instead of runtime assemblies,
the SDK source generators (Lib's `[GeneratedRegex]` fails without them), generator base directory = `obj/<cfg>/<tfm>`
(it feeds generated type names), `PackageReference Aliases` (Lib's `extern alias skiasvg`), Roslyn's internal
debug-plus mode that csc sets for `/debug+` (internal, set by reflection), and the SDK's exact formatting of
generated files.

**Timing** (same machine; `dotnet build` with its build server already warm):

| | `dotnet build`, one file touched | in-process, warm | in-process, first compile |
|---|---|---|---|
| Playground (15 files) | ~500 ms | 40–60 ms | ~1.0 s |
| Wake (42 files) | ~500 ms | ~100 ms | ~1.1 s |
| Lib (1,043 files) | 1.8 s | 3.1–3.7 s | 4.3 s |

User projects recompile 5–10× faster once warm. Lib is slower, but Lib is precompiled in releases and repo
developers keep `dotnet build`. The spike re-reads all 167 reference assemblies on every compile; caching them
will cut the warm numbers further.

## What ships

`ref/net10.0/*.dll` (4.5 MB) and `analyzers/dotnet/cs/*.dll` (1.7 MB, source generators) from the NuGet package
`Microsoft.NETCore.App.Ref` matching the bundled runtime's major version. MIT licensed. `Editor.csproj` takes them
with `PackageDownload` and copies them to `<app>/CompilerSupport/`.

## Design

- `Editor/Compilation/ProjectModel.cs` + `InProcessCompiler.cs`, ported from the spike (minus the comparison
  tools, which move into a test project).
- **Which path per project** (in `Compiler`): in-process when the project has no `PackageReference`/
  `ProjectReference` and `ProjectModel.Warnings` is empty. Otherwise `dotnet build` if the SDK is present, else a
  clear error naming what needs the SDK ("this project references NuGet package X").
- TiXL writes user csproj files itself (`ProjectXml`), so the supported subset is defined by `ProjectXml`. A test
  evaluates every csproj shape `ProjectXml` can produce and requires zero warnings.
- Copy `Content`/`None` items with `CopyToOutputDirectory` (user `dependencies/**` natives).
- Cache `MetadataReference`s for the reference pack and `T3_ASSEMBLY_PATH` assemblies; load generators once.
- Debug-plus: internal `WithDebugPlusMode`; if a Roslyn update removes it, fall back silently (only debugging
  fidelity is affected, IL was the same in all 20 projects).
- Startup check (`DependencyCheck.CheckDotNetSdk`): no longer required. Report the SDK only when a loaded
  project needs the CLI path.
- Repo Debug builds (Rider, `dotnet build`): unchanged.

## Not covered by the spike (verify during integration)

1. Loading the in-process DLL in the running editor: hot reload, shadow copy, `SourceManifest`, unloading. IL is
   identical, so behaviour should be too, but writing and swapping the files is a new code path.
2. Windows and macOS. The code is platform-neutral; generator loading and paths are untested there.
3. Memory in the editor process: Roslyn allocates per compile (tens of MB for small projects). Compiling is a
   user action that already blocks; check for a GC hitch afterwards.
4. Debugging user operators in Rider with the in-process PDB (same format and options, untested).
5. Hand-edited csproj files with custom targets or NuGet packages: CLI fallback, or an error without SDK.
   Own restore could come later with the HTTP code from Plan_OptionalPackages.

## Phases

1. **Port + equivalence test.** Integrate behind `UserSettings` (default on in Release, off in Debug). A test
   project compiles fixture projects both ways when an SDK is present and compares with `MetadataDump`; it runs in
   CI on all three platforms.
2. **Ship CompilerSupport**, relax the startup check, editor test round on Linux, macOS, Windows (including Rider
   debugging of a user op).
3. **Drop the SDK from user setup on all platforms at once**: Windows installer SDK step, `install-dependencies.sh`,
   DMG instructions, AUR/Nix runtime dependencies (keep as optional), docs.

Rough effort: 1–2 weeks, most of it in phase 1 and the editor integration points.

## Decisions (2026-11-23)

1. **Ships in 4.4**, with the `dotnet build` fallback kept for projects that need it.
2. **All three platforms together**, including the Windows installer. Platform-specific deploy paths would add release
   complexity for no gain: the fallback behaves the same everywhere, and Windows stable 4.4 already ships after the
   others (signing), so Windows pre-releases test the new path in the meantime.
