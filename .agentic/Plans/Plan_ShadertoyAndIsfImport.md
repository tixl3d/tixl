# Shadertoy and ISF import

Status: **research only** (2026-11-27). Nothing built. Feasibility tested with slangc 2026.18 (the pinned version).

## Goal

Let users bring existing GLSL fragment shaders into TiXL with as little hand-porting as possible:

1. **Shadertoy**: paste a `mainImage` shader and see it run. Later, import multipass shaders (Buffer A–D, Common).
2. **ISF** (Interactive Shader Format, VIDVOX; used by VDMX, Magic, Resolume-adjacent tools): import a `.fs` file
   as an operator whose inputs come from the file's JSON header. Large free libraries of ISF files exist, and
   their typed inputs map well onto TiXL parameters.

This replaces item 3 ("Import from ShaderToy … translate GLSL → HLSL") in `Plan_NewSymbolAndImport`, and removes
the translation step.

## Key finding: no GLSL→HLSL translator needed

Verified on 2026-11-27, Linux:

- **Slang compiles verbatim GLSL inside a TiXL HLSL template** when `-allow-glsl` is added to TiXL's own argument
  list (`SlangShaderCompiler.BuildArguments`: `-fvk-use-dx-layout`, the `-fvk-*-shift`s, `sm_5_0`, `spirv_1_5`). The
  test file had the HLSL cbuffer/`Texture2D`/`psMain` of `CustomImageShader-template.hlsl` and an unmodified
  Shadertoy `mainImage` with `mat2`, `uv *= rot(a)`, `mix`, `fract`, `mod` and `texture(iChannel0, uv)`. It produced
  SPIR-V. Without the flag it fails with undefined identifiers.
- **The translation is type-aware**: `-target hlsl` on a pure GLSL file showed `uv *= rot(a)` becoming
  `mul(rot(a), uv)` under `#pragma pack_matrix(column_major)`, and `mod` keeping GLSL semantics
  (`x - y*floor(x/y)`), not HLSL `fmod`. These two cases are what regex translators get wrong.
- **ISF-style code also compiles**: `gl_FragColor` and `isf_FragNormCoord` as `static` globals, `IMG_NORM_PIXEL`,
  `IMG_THIS_PIXEL`, `IMG_SIZE` as macros/functions over `Texture2D`, JSON inputs as cbuffer members, ternaries over
  `vec4`. Legacy `texture2D(img, uv)` fails (E30019) until a `texture2D(Texture2D<float4>, vec2)` shim overload is
  added to the prelude.
- **`-allow-glsl` must be opt-in per shader, not global.** All 60 existing `psMain` shaders tested still compile with
  it, but 53 of them produce *different* SPIR-V. Why was not investigated. Enabling it for every shader would be an
  untested change to every effect in the library.
- **No D3D11 path.** `-target hlsl`/`dxbc` rejects the mixed file (E36107, `Std140DataLayout`). Vulkan is the default
  backend on all platforms (`Editor/App/ProgramWindows.cs:164-184`); D3D11 is the Windows-only `TIXL_BACKEND=d3d11`
  fallback. GLSL ops show a clear error there instead of compiling.
- Slang's HLSL output is correct but machine-shaped (`_0` suffixes, `#line`, temporaries inlined, `0.1` as
  `0.10000000149011612f`). It is not usable as a human-editable "convert to HLSL" result.

Consequence: the user's code **stays GLSL**, verbatim, and can be copied back and forth with Shadertoy. Only a prelude
in the template adapts names.

## What exists today

- `[CustomPixelShader]` (`Operators/Lib/Symbols/image/use/CustomPixelShader.*`) is a composition: `ReadFile`
  (template) → `SearchAndReplace` (`/*{method}*/`, `/*{defines}*/`) → `PixelShaderFromSource`, plus
  `FloatsToBuffer`/`IntsToBuffer`, `RenderTarget`, `SamplerState`s, `GradientsToTexture`. Template:
  `Lib:shaders/img/use/CustomImageShader-template.hlsl`. A new op can copy this composition with another template.
- `ShaderCompilationArgs` (`Core/Resource/ShaderCompiling/ShaderCompiler.Main.cs:207`) carries source, entry point,
  owner and name. There is no per-shader compiler option. `SlangShaderCompiler` already scans the source text for
  includes (`GetIncludesFrom`), so scanning it for an opt-in marker fits.
- Code inputs: `Plan_CodeInputs` (Phase 2 built) gives `// A: Scale 0..1` annotations and `UsageType.Code` with a
  `CodeLanguage`. GLSL needs a `CodeLanguage` value for highlighting later; annotations work as they are.
- Feedback: `[KeepPreviousFrame]` (`Lib/image/use`) gives last frame's texture, for multipass self-reads and ISF
  `PERSISTENT` buffers.
- Graph paste (`Editor/UiModel/Modification/NodeActions.cs:250`) only accepts TiXL JSON. External file drops go to
  `DragAndDropHandling.CompleteExternalDrop` (`Editor/App/SdlImGuiInput.cs:331`).
- Adding typed inputs to a symbol from the editor: `InputsAndOutputs.AddInputToSymbol`
  (`Editor/UiModel/Modification/InputsAndOutputs.cs:181`).

## Design

### Part 1: GLSL opt-in in the compiler (small, `Core`)

A marker line in the source, e.g. `#define TIXL_GLSL 1`, set by the GLSL templates. It is valid for both compilers,
so it does not need a new field on `ShaderCompilationArgs`.

- `SlangShaderCompiler`: if the source defines `TIXL_GLSL`, add `-allow-glsl`.
- `DX11ShaderCompiler`: if the source defines it, fail with "GLSL shaders need the Vulkan backend".
- Test in `Core.Tests/SlangShaderCompilerTests.cs`: one GLSL-in-template shader compiles. A plain HLSL shader's
  SPIR-V is unchanged, which guards against the flag leaking.

This is the only `Core` change. It is justified because the Player must compile these shaders too.

### Part 2: `[ShadertoyShader]` op (single pass)

New op in `Lib/image/generate` (Shadertoy shaders are mostly generators), built like `CustomPixelShader`.

Template `Lib:shaders/img/generate/ShadertoyShader-template.hlsl` with a prelude:

| Shadertoy | TiXL source |
|---|---|
| `iResolution` (vec3, z = 1) | render target size |
| `iTime`, `iTimeDelta`, `iFrame`, `iFrameRate` | context time (as `CustomPointShader` gets `Time`), a frame counter in the op |
| `iMouse` | `Mouse` input (Vector4); default 0 |
| `iDate` | system clock |
| `iChannel0..3` | `Channel0..3` Texture2D inputs, wrapped in a shim struct so `texture()`, `textureLod()`, `texelFetch()`, `textureSize()` resolve |
| `iChannelResolution[4]`, `iChannelTime[4]` | texture sizes; channel time = `iTime` |
| `fragCoord`, `fragColor` | `psMain` calls `mainImage(c, fragCoord)` |

**Y orientation** (verify visually): Shadertoy's `fragCoord` origin is bottom-left; TiXL `uv` is top-left. Use
`fragCoord = float2(uv.x, 1 - uv.y) * size`, and flip v inside the `texture()`/`texelFetch()` shims. Images then
look upright, and a buffer read back by another `ShadertoyShader` stays consistent.

Inputs: `Channel0..3`, `Mouse`, `A–D` (annotations still useful for tweaks), `ShaderCode` (GLSL, `UsageType.Code`),
`CommonCode` (Shadertoy's Common tab, inserted at global scope like `AdditionalCode`), `Resolution`,
`TextureFormat`, `GenerateMips`. Phase 1 uses one linear-wrap sampler for all channels; per-channel filter and wrap
come with multipass import.

Default code: our own short gradient example, not Shadertoy's text.

**Paste UX:** pasting into the code field works without extra code. Cheap extra: in graph paste, if the clipboard is
not TiXL JSON and contains `void mainImage(`, create a `ShadertoyShader` at the mouse with that code.

### Part 3: multipass Shadertoy

Input: the Shadertoy JSON of a shader (`renderpass[]` with `inputs`/`outputs`), pasted or loaded from a file.

- One `ShadertoyShader` per pass (`Buffer A–D`, `Image`), `Common` copied into each `CommonCode`.
- Channel wiring follows Shadertoy's per-frame order A, B, C, D, Image. A pass that reads itself, or a buffer later in
  the order, gets last frame's result through `[KeepPreviousFrame]`; earlier buffers are wired directly.
- Buffers default to `R16G16B16A16_Float` (Shadertoy buffers are float).
- Per-channel `sampler` settings (filter, wrap, vflip) → `SamplerState` ops on the `CustomSampler` style inputs.
- Unsupported in this phase (placeholder texture plus a warning): keyboard, webcam, video, music/soundcloud, cubemap
  passes, the Sound pass. Shadertoy's built-in texture assets can't be redistributed; ask the user to supply them.

**Fetching by URL:** shadertoy.com refused automated fetches (HTTP 403) during research, so the API details below are
from memory and need checking: `GET https://www.shadertoy.com/api/v1/shaders/{id}?key={appkey}`, a per-user app key,
and only shaders their authors published as "Public + API". Recommendation: accept pasted JSON and source first; add
URL fetch with a user-supplied key in settings later, if users ask for it.

### Part 4: ISF import

ISF facts (from docs.isf.video, read 2026-11-27): a `/* { JSON } */` header with `ISFVSN`, `DESCRIPTION`, `CREDIT`,
`CATEGORIES`, `INPUTS`, optional `PASSES` and `IMPORTED`. Input `TYPE`s: `event`, `bool`, `long` (`VALUES`/`LABELS`
menu), `float`, `point2D`, `color`, `image`, `audio`, `audioFFT`, with `DEFAULT`/`MIN`/`MAX`/`IDENTITY`/`LABEL`.
Built-ins: `TIME`, `TIMEDELTA`, `RENDERSIZE`, `PASSINDEX`, `FRAMEINDEX`, `DATE`, `isf_FragNormCoord` (bottom-left
origin). Functions: `IMG_PIXEL`, `IMG_NORM_PIXEL`, `IMG_THIS_PIXEL`, `IMG_NORM_THIS_PIXEL`, `IMG_SIZE`. Filters are
recognised by an `image` input named `inputImage`; transitions by `startImage`/`endImage`/`progress`; anything else
is a generator. An optional `.vs` vertex shader shares the base name. The docs do not name the fragment file
extension; `.fs` is the convention in the wild.

ISF's value is its **named, typed, ranged inputs**. Two ways to carry them:

- (a) A generic `[IsfShader]` op that maps inputs onto `A–D`/`Offset` with generated annotations. This is lossy:
  ISF files often have more than four floats, plus colors, menus, bools, events and several images.
- (b) **Recommended:** importing creates **a new symbol per ISF file** in the focused project, with real inputs:

| ISF | TiXL input | UI |
|---|---|---|
| `float` | `float` | Min/Max/default from JSON |
| `bool`, `event` | `bool` (`event` as trigger) | |
| `long` | `int` | dropdown with `LABELS`/`VALUES` (check whether int inputs support custom labels; else a string dropdown) |
| `color` | `Vector4` | color picker |
| `point2D` | `Vector2` | |
| `image` | `Texture2D` | `inputImage` first, so filters chain like other image ops |
| `audio`, `audioFFT` | `Texture2D` | connect a TiXL audio-to-texture op |

The generated template declares the cbuffer from `INPUTS` in declaration order (HLSL packing, matching
`FloatsToBuffer`), the image inputs and the ISF prelude, and contains the ISF body verbatim. `DESCRIPTION`/`CREDIT`
become the op description. Symbol creation shares infrastructure with "New Symbol" in `Plan_NewSymbolAndImport`.

- `PASSES` → one pass per draw inside the symbol's composition with `PASSINDEX` as a constant; `PERSISTENT` targets
  via `[KeepPreviousFrame]`; `FLOAT` → float format. `WIDTH`/`HEIGHT` expressions (`"$WIDTH/16.0"`, `$<input>`):
  support `$WIDTH`, `$HEIGHT`, inputs and `+ - * /` at first. Anything more complex falls back to full size with a
  warning.
- `IMPORTED` images → `LoadImage` with the path copied into the project's assets.
- `.vs` vertex shaders: not supported at first; warn and use the default quad.
- Entry points: drop `.fs` files on the graph; drop a folder of them to batch-import into one namespace (this reuses
  the `FolderImportDialog` path).

## Phases

| Phase | Scope | Size |
|---|---|---|
| 1 | Part 1 compiler opt-in + test | S |
| 2 | `[ShadertoyShader]`, prelude, Y-flip check, docs section in `UsingCustomShaders.md`, manual test set | M |
| 3 | Graph paste of `mainImage` code; GLSL `CodeLanguage` for the code editor | S |
| 4 | ISF single-pass import as generated symbols, `.fs` drop | M–L |
| 5 | ISF `PASSES`/`PERSISTENT`/`IMPORTED`; Shadertoy multipass from JSON | L |
| 6 | Optional: Shadertoy URL fetch with app key; audio channels | S–M |

## Open decisions

1. **New op or a GLSL mode on `CustomPixelShader`?** Recommendation: a new op. The variable contract
   (`fragColor`/`fragCoord` vs `c`/`uv`), the inputs and the presets differ. A mode switch would make presets fail
   silently when toggled.
2. **Op name.** `ShadertoyShader` is the name users search for. `CustomGlslShader` avoids naming another product.
   Recommendation: `ShadertoyShader`, with "GLSL" in the description and search tags.
3. **ISF route (a) or (b).** Recommendation: (b).
4. **Licensing in the UI.** Shadertoy's default licence is, from memory, CC BY-NC-SA 3.0 (verify on
   shadertoy.com/terms). The importer should keep author, URL and licence as a header comment. It should also warn
   once that non-commercial licences apply to commercial shows, the same concern as the BASS licence. Don't ship
   imported Shadertoys as presets unless they're permissively licensed.
5. **D3D11 fallback:** error only (recommended), or route GLSL shaders through Slang even there later.

## Risks

- **Performance surprise.** Many Shadertoys are full-screen raymarchers tuned for 800×450 previews; at 4K they can
  stall the editor UI, which is synced to rendering. Consider a default `Resolution` scale of 0.5 for imported
  shaders, and mention it in the docs.
- **Untested SPIR-V difference.** Why `-allow-glsl` changes SPIR-V for plain HLSL is unknown. The opt-in keeps that
  away from existing shaders, but GLSL templates should get one visual test.
- **Slang GLSL coverage.** GLSL features Slang's compatibility mode lacks will surface as compile errors in user code.
  Collect a test corpus of ~20 popular Shadertoys and ISF files (with compatible licences) before Phase 4.

## Ideas for later

- One-way "Convert to `CustomPixelShader`" once a readable translator exists. Slang's output isn't readable enough.
- `iChannel` music input → TiXL's audio analysis texture, so audio-reactive Shadertoys work.
- Show the ISF `CATEGORIES` in the symbol library.
