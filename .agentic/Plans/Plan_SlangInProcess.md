# Plan: Compile shaders with Slang in-process instead of spawning slangc

Status: proposal (2026-10-09). Deferred until Vulkan rendering and features are stable; this is a pure
performance change with no visible behavior.

## Symptom

`SlangShaderCompiler` starts one `slangc` process per (file, entry point, stage). On Windows a cold
AllTests run is 5–10× slower than on macOS / Linux.

Measured on Windows 11, Defender real-time protection on, Slang 2026.18:

| Call                                   | Wall    |
|----------------------------------------|---------|
| `slangc -v` (no compilation)           | ~0.30 s |
| trivial pixel shader → SPIR-V          | ~0.35–0.65 s |
| median entry point on Linux (survey)   | ~0.18 s |

Almost all of the Windows cost is process creation plus loading the 25 MB `slang-compiler.dll` (and
Defender scanning it, and the temp source / `.spv` / `.json` files written and read per compile). The actual
compilation is a small fraction. The disk shader cache (`ShaderCompiler.Caching`) hides this on warm runs,
but every cold start, cache clear, Slang version bump or shader edit pays it.

## Proposal

Load `slang.dll` (`libslang.so` / `libslang.dylib`) once and compile through its API.

### Step 1 — Drop-in replacement through the flat C API

Slang still exports the C compile-request API (`spCreateSession`, `spCreateCompileRequest`,
`spProcessCommandLineArguments`, `spCompile`, `spGetDiagnosticOutput`, `spGetEntryPointCodeBlob`,
`spGetReflection`). It is marked deprecated in favour of the COM-style `IGlobalSession` API, but it is plain
C, so it needs only `[LibraryImport]` declarations — no vtable interop — and it accepts the exact argument
list `BuildArguments` already produces. That keeps the change small and the output identical:

1. `SlangNative` (internal, in `Core/Resource/ShaderCompiling/`): `LibraryImport`s for the functions above,
   resolved with `NativeLibrary.SetDllImportResolver` from the same places `FindCompiler` looks
   (`TIXL_SLANGC`'s folder, `<app>/slang/bin`, the pinned install, then the OS default search).
2. One global session, created lazily on first compile. Creating it loads Slang's core module and costs
   roughly what one `slangc` launch does, so it must be paid once, not per shader.
3. Per compile: create a request, pass `BuildArguments(...)` minus `-o` / `-reflection-json`, add the source
   as a string (`spAddTranslationUnitSourceString` with `args.Name` as the path, so diagnostics and relative
   includes keep working), compile, read SPIR-V from the code blob.
4. Reflection: verify that the C API can still produce the JSON `-reflection-json` writes
   (`spReflection_ToJson` or equivalent). If yes, `ReadBindings` stays unchanged. If not, port
   `ReadBindings` to walk `spReflection_*` directly — it only needs entry-point bindings with their `used`
   flag and the thread-group size.
5. No temp files at all: source, SPIR-V and reflection stay in memory.

Keep the process path behind `TIXL_SLANG_PROCESS=1` for one release, for A/B comparison and as a fallback if
a driver or platform misbehaves.

### Step 2 — Optional, only if profiling asks for it

- Custom `ISlangFileSystem` that resolves `#include` through the asset registry instead of passing every
  package folder as `-I`. Needs the COM API.
- Parallel compilation: one session per worker thread (sessions are not thread-safe; the global session can
  be shared for creating them, guarded by a lock).

## Things to watch

- **Crash isolation is lost.** A Slang assertion or crash on odd input now takes down the editor instead of
  one `slangc` process. Mitigation: the process fallback above, and the shader survey run before upgrading
  the pinned version.
- **Threading.** `TryCompileShaderFromSource` can be called from more than one thread (the temp file names
  already include the managed thread id). Start with a single lock around compiles; measure before going
  per-thread.
- **Native loading on Linux / macOS.** Distribution packages (`shader-slang` on Arch, Nix) put `libslang`
  on the default search path; the tarball and DMG use the pinned install. The resolver must try the pinned
  folder first so a mismatched system version is not picked up silently. `DependencyCheck` should check the
  library (and its version via `spGetBuildTagString`) instead of `slangc`.
- **Bundling.** The Windows installer already ships `slang.dll`, `slang-compiler.dll`, `slang-glslang.dll`
  and `slang-glsl-module.dll` next to `slangc.exe` (`Installer/Windows/build-release.ps1`); the macOS and
  Linux bundles need the matching libraries.
- **Cache key.** Output should be byte-identical to `slangc`; `CacheTarget` stays `spirv-slang-<version>`.

## Verification

1. Shader survey (`Spikes/ShaderSurvey/shader_survey.py` or a C# equivalent in `Core.Tests`): compile every
   referenced entry point both ways and compare SPIR-V bytes and extracted bindings. Expect identical.
2. `Core.Tests` `SlangShaderCompilerTests` pass on Windows, Linux and macOS.
3. Cold AllTests run on Windows with the shader cache cleared: record wall time before and after.
4. Visual reference suite (rendering path is touched indirectly via shader blobs).

## Estimate

Step 1: 1–2 agent days including the survey comparison. Step 2: only on demand.
