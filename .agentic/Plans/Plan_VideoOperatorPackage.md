# Extract a `Video` Operator Package (slim media natives out of Lib)

**Status:** Draft — 2026-06-19. Design only, no code yet. Spun out of the FFmpeg encode work
([`Plan_FfmpegEncode.md`](Plan_FfmpegEncode.md)) — the cross-context encoder-registration problem exposed
that FFmpeg shouldn't be coupled to Lib at all.

## Goal

Move the FFmpeg video operators (then swiftcam, then the OpenCV camera ops) out of **Lib** into a new
**`Video`** operator package, and rename the FFmpeg infra assembly so the name `Video` is free for the
package. Outcome:

- **Lib drops its `Video.csproj` reference** → no FFmpeg on Lib's load/hot-reload path (the exact
  assembly-load/native-resolver surface behind most real-world TiXL exceptions), for a feature that's <1% of
  use. This is the headline win.
- **Non-video projects load no FFmpeg natives** → faster startup, fewer native-failure points for the majority.
- **Encoder registration gets a proper home:** referencing the `Video` package loads it → it registers the
  encode factory into Core. No Lib `[ModuleInitializer]` hack, no editor reflection.

## Why now

The encode milestone needs the editor's render-export to reach an FFmpeg encoder that lives in the operator
load context (the editor can't depend on it — that would duplicate the ~100 MB natives). The stop-gap was a
`[ModuleInitializer]` in **Lib** forcing `Video.dll` to load eagerly on every Lib load/reload — wrong place
for a <1% feature on the most-edited package. Making `Video` its own package removes the coupling entirely:
the package is loaded only by projects that reference it, and that load is where registration belongs.
*(The Lib hack has been removed; `VideoExport.Factory` is simply unset until this lands.)*

## Current state (verified)

- **Infra** — `Video.csproj` (root, namespace `T3.Video`): `FfmpegLibrary`, decode (`VideoDecoderSession` +
  converters + `VideoFrameCache`), playback (`VideoPlaybackEngine`/`Controller`), and the new encode
  (`VideoFileEncoder`, `HardwareEncoderProbe`, `FfmpegVideoExport`). Ships FFmpeg natives via the `FFmpeg.LGPL`
  package + the `FlattenFFmpegNatives` target. *Not an operator package* — a plain class library.
- **Video operators** live in [`Operators/Lib/io/video/`](../../Operators/Lib/io/video/), by native dep:
  - **FFmpeg** (`Video.csproj`): `PlayVideo`, `VideoClip`, `VideoClipPlayer`, `VideoStreamInput`
    (+ `_ProcessVideoClips`, `_VideoClipObsolete`). **Only these pull in `Video.csproj`.**
  - **swiftcam** (`swiftcam.dll`): `SwiftCamDevice` (+ `swiftcam/`).
  - **OpenCV** (EmguCV): `VideoDeviceInput`, `CameraCalibrator`.
  - **audio** (BASS): `PlayAudioClip` — **stays in Lib** (it's audio, not video).
- **OpenCV is entangled beyond `io/video/`:** also used by `io/ptz/OnvifCamera.cs` and
  `io/dmx/helpers/Video2DPointScanner.cs`. So moving the video-folder OpenCV ops **does not** let Lib drop
  EmguCV — PTZ/DMX keep it. FFmpeg has no such entanglement (clean).
- **Package model** (from [`spout.csproj`](../../Operators/Spout/spout.csproj)): each package is a csproj with a
  `PackageId` GUID, `RootNamespace`/`AssemblyName` (e.g. `t3.spout`), a curated **`<Using>` block** (the
  operator global-usings — moved `.cs` files need it), native deps via `<Content Include="./dependencies/**">`,
  and a `<Operators Include="lib"/>`-style dependency list emitted to `OperatorPackage.json`.
- **Symbol binding is by GUID, not namespace.** A `.t3` stores only `"Id": "<guid>"` (verified in
  `PlayVideo.t3`); the class binds via its `[Guid(...)]` attribute. **So an operator can change
  namespace/package and still resolve** — no `.t3` rewrites for the move. Symbols resolve globally once a
  package is loaded; the per-project `<Operators Include>` list controls *which* packages load.
- `PlayerExporter` maps op GUIDs → native DLLs (e.g. `VideoDeviceInput` → opencv).

## Naming

- **Op package:** `Video` — `RootNamespace`/`AssemblyName` `t3.video` (matching `t3.spout`); what Examples and
  projects reference.
- **Infra (rename):** `Video.csproj` / `T3.Video` → **`VideoServices.csproj` / `T3.VideoServices`** (frees the
  `Video` name; "Services" per the agreed hunch). Alternative considered: `VideoEngine`.
- **Core facades stay `T3.Core.Video`** (`VideoPlayback`, `VideoExport`). An op would
  `using T3.VideoServices;` + `using T3.Core.Video;`.

## Design

- **New `Operators/Video/` package** mirroring the spout structure: new `PackageId`, `RootNamespace t3.video`,
  the operator `<Using>` block (copied from Lib), `ProjectReference` to Core + Logging + `VideoServices`,
  the FFmpeg native `Content`/flatten, and `<Operators Include="lib"/>` + `Types` (the moved ops use Lib base
  types). Consumers reference it via `<Operators Include="Video"/>`.
- **Registration lands here.** A `[ModuleInitializer]` (or package-load hook) in the `Video` package calls
  `FfmpegVideoEncoderFactory.Register()`. Because consuming projects reference the package, it loads and
  registers — covering "export with no video op in the graph." *(Open: confirm the initializer fires on
  package *load*, not only on first op instantiation; if not, the fallback is the editor finding the now-cleanly-
  identifiable `t3.video` package assembly at export and calling `Register()` — still off Lib's path.)*
- **Lib slims:** drop the `Video.csproj` reference, the `FFmpeg.LGPL` package ref, and the `FlattenFFmpegNatives`
  target (all move to the `Video` package). swiftcam.dll + EmguCV move in later phases with their ops.
- **`PlayerExporter`:** the video op GUIDs now belong to the `Video` package; FFmpeg natives bundle when that
  package is used. Prefer package-level native bundling over per-GUID mapping where possible.

## Phasing (each build-verifiable)

1. **Infra rename `Video` → `VideoServices` — DONE & VERIFIED.** Namespace `T3.Video` → `T3.VideoServices`
   (22 files, binary-mode to preserve line endings), folders `Video/`→`VideoServices/` and
   `Video.Tests/`→`VideoServices.Tests/`, csproj/assembly renamed, and every reference updated (`t3.sln`,
   Lib's `ProjectReference`, the test's `ProjectReference`, `InternalsVisibleTo`). 32/32 tests pass; Lib builds
   and `VideoServices.dll` ships in the operator output. No behavior change.
2. **Create the empty `Video` package — DONE & VERIFIED.**
   [`Operators/Video/Video.csproj`](../../Operators/Video/Video.csproj) mirrors Lib's structure (the operator
   `<Using>` block verbatim, a fresh `PackageId`, `RootNamespace`/assembly `Video`, refs to
   Core+Logging+VideoServices, `<Operators Include="lib"/>`+`Types`, and the package-info/content targets).
   Builds standalone → `Video.dll` + `OperatorPackage.json`; registered in `t3.sln` (project entry + config
   block; solution parses). **Deliberately *not* yet in the Editor/Player `ProjectDependencies`** so the editor
   doesn't load an empty package — that wiring lands in Phase 3 with the operators.
3. **Move the FFmpeg cluster — DONE (build-verified; in-editor verify pending).** All 6 (`PlayVideo`,
   `VideoClip`, `VideoClipPlayer`, `VideoStreamInput`, `_ProcessVideoClips`, `_VideoClipObsolete` —
   `.cs`/`.t3`/`.t3ui`, 18 files) → `Operators/Video/lib/io/video/`. **Namespaces kept `Lib.io.video`** —
   it's a *shared* namespace (Ndi/Spout/Mediapipe already ship ops into it from their own packages), i.e. the
   operator-browser location, not a per-package name; keeping it preserves where users find the ops and needs
   no `.t3` edits. **Lib dropped the `VideoServices` reference** (its only 3 users moved out). examples gained
   `<Operators Include="Video"/>`; the Editor's `.sln` `ProjectDependencies` gained Video. Lib + Video +
   examples + **Editor all build**. *In-editor verify (user): editor loads the package, ops resolve
   (cross-package GUID), a video plays, FFmpeg natives load from the Video output, and a no-Video-dep project
   loads no FFmpeg.*
4. **Move FFmpeg native shipping — DONE (the natives half).** `Sdcb.FFmpeg` + `FFmpeg.LGPL` + the
   `FlattenFFmpegNatives` target moved Lib → `Video`; verified `Video/bin` co-locates `Video.dll` +
   `VideoServices.dll` + the av/sw natives, so `FfmpegLibrary` (next to `VideoServices.dll`) resolves them.
   **`PlayerExporter` — NO CHANGE NEEDED (verified).** The export collects each *used* symbol's `SymbolPackage`
   (`ExportData`) and copies that package's whole output directory (`TryExportSymbolPackages`), so a project
   using a video op bundles the `Video` package **with its co-located FFmpeg natives automatically** — no
   GUID→DLL mapping required. And since the natives left Lib, a **non-video project now ships no FFmpeg at all**
   (Player.csproj has no FFmpeg ref; the player base carries none), which is smaller than before. Only fix: a
   stale comment in `PlayerExporter._dependencyDefinitions` (said the LGPL build ships with Lib → now Video).
   *Verify in a real export: a video project's player decodes; a non-video one carries no av*.dll.*
5. **Registration in `Video`** → `Register()`. *(Lib hack already removed.)* Then **resume encode wiring**
   ([`Plan_FfmpegEncode.md`](Plan_FfmpegEncode.md) 1c-ii) with registration in the right place.
6. **Migration:** add `<Operators Include="Video"/>` to `examples.csproj` (+ any first-party project using
   video) and the default new-project template; decide editor handling for existing user projects that use
   video ops (auto-add the dependency vs. a "missing package" prompt).
7. **Later:** move `SwiftCamDevice` (+ swiftcam.dll) into `Video`. **Later/separate:** the OpenCV camera ops —
   but Lib only sheds EmguCV once `OnvifCamera` (PTZ) and `Video2DPointScanner` (DMX) also move or drop OpenCV,
   which is its own decision (they aren't "video").

## Risks / open questions

- **Existing user-project migration** — the biggest one. Projects using video ops need `<Operators Include="Video"/>`.
  Without it the package isn't loaded and those GUIDs won't resolve. Need an editor story (auto-migrate on load,
  or a clear prompt). First-party (Examples/template) we just edit.
- **Module-initializer-on-package-load** — confirm it fires when the package *loads* (referenced) vs only on
  first op instantiation. Fallback: editor on-demand find at export (now clean — `t3.video` is its own package).
- **Cross-package types** — the moved ops likely use Lib base types/utilities; the `Video` package references
  Lib (`Video → Lib`, consumers → both; no cycle). Confirm no Lib type they need is `internal`.
- **Operator `<Using>` parity** — the moved `.cs` rely on Lib's global-usings block; the `Video` package must
  replicate it or the files won't compile. (Low risk, mechanical.)
- **Infra rename churn** touches the just-written encode code — mechanical but wide.
- **OpenCV entanglement** (PTZ/DMX) blocks full Lib OpenCV removal — scope creep if pursued now; keep separate.
- **Hot-reload of `Video`** uses the same ALC machinery, but now isolated from Lib's reload loop (the point).

## Key files

| Concern | File |
|---|---|
| Sibling package template to copy | `Operators/Spout/spout.csproj` |
| Lib package to slim (drop Video ref + natives) | `Operators/Lib/Lib.csproj` |
| FFmpeg infra (renamed → `VideoServices`, done) | `VideoServices/VideoServices.csproj` |
| FFmpeg ops to move | `Operators/Lib/io/video/{PlayVideo,VideoClip,VideoClipPlayer,VideoStreamInput,_ProcessVideoClips,_VideoClipObsolete}.*` |
| Encoder registration entry (to call from `Video`) | `Video/FfmpegVideoExport.cs` (`FfmpegVideoEncoderFactory.Register`) |
| Native-DLL export mapping | `Editor/UiModel/Exporting/PlayerExporter.cs` |
| Consumers to migrate | `Operators/examples/examples.csproj`, new-project template |

## Manual test

After the FFmpeg-cluster move: a project referencing `Video` plays a video and exports; a project *not*
referencing it has no video ops and loads no FFmpeg; a saved project that used `PlayVideo` still resolves it
once the `Video` dependency is present.
