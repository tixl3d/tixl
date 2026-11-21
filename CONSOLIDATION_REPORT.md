# CrossTiXL consolidation report — `crosstixlultimate`

Date: 2026-11-17 · Target: `D:\CrossTixlClaude` · Branch: `crosstixlultimate` (local, **not pushed**)

| | |
|---|---|
| Base | `tixl3d/tixl` `main` @ `3459907` ("op: [SkinMeshExample]", 2026-11-17) |
| `origin/crosstixlultimate` before | `3459907` — identical to upstream main, no custom commits to preserve |
| Main feature line merged | `main-collective` @ `4748c85` from `D:\CrossTixlDEV\tixl` (2026-11-12, identical to `origin/main-collective`) |
| Result vs upstream main | 1135 files changed · 122 new operators (`.t3`) · 34 operators with graph changes · 12 with C#-only changes |
| Build (Linux, .NET SDK 10.0.115) | `dotnet restore` OK · `dotnet build t3.sln -c Release` from clean: **0 errors** · `-c Debug`: **0 errors** |

## Commits on top of upstream

| Commit | What |
|---|---|
| `merge: main-collective (CrossTixlDEV) onto latest tixl3d/tixl main` | All feature work (see below). 3 conflicts resolved (see "Upstream conflicts"). |
| `build: restore StreamDiffusion to the solution and drop duplicate package entries` | Fixes a Release-build failure that already existed on main-collective. |
| `port: stable/auto-startup-dmx improvements from D:\DEVDEVDEV (Oct 31) onto the facade` | Readback retry, OpTimings, PointsToDmxLights, ArtnetOutput. |
| `docs: DepthToPosition and DepthParallax sections with screenshots` | Recovered from DEVDEVDEV. |
| `fix: give the Video package's [VideoDeviceInput] its own symbol Guid and restore its .t3` | Fixes a duplicate-Guid / missing-.t3 problem that already existed on main-collective. |
| `docs: CONSOLIDATION_REPORT.md` | This file. |
| `feat: showNET laser output …` + `fix: review findings for the showNET transport + docs` | Cherry-picked from `D:\CrossTixlDEV\tixl` branch `feat/laser-shownet` (2026-11-18, `c8d59ee`, `3469726`): showNET (LaserAnimation Sollinger PHOENIX) as the 4th laser plug transport — protocol layer, UDP stream with handshake/configure/frames, discovery on UDP 41856, plug settings, Output Setup UI, docs, 14 protocol tests. Applied cleanly. |
| `fix: showNET delta encoder allocates its point scratch once per frame` | Moves a per-point `stackalloc` out of the encoder loop (CA2014, stack growth on large frames). Core.Tests ShowNet/Laser: 116/116. |
| `fix: laser camera gizmos - every camera shows, in the right place, with its real view` | Output-window overlay with gizmos on: projects through the shown image size instead of the editor backbuffer (markers were offset/scaled), instantiates laser cameras nothing evaluates so all of them appear, and builds each frame from the camera's real view/projection matrices (Up input, Rotation aim, reference camera, square field). |
| `fix: laser camera welds edges by position, culls hidden lines, warns over budget` | Mesh edges dedupe by welded vertex position (a per-face-normal cube no longer scans every shared edge twice), new CreaseAngle / BackfaceCulling inputs drop flat seams and the mesh's hidden side per camera, and the op's status warns when the scene exceeds the frame budget instead of silently truncating. |
| `fix: review findings for the laser weld, showNET discovery and oversized frames` | Review pass over the laser features: the welded edge pair is canonicalized after the remap (the raw-index min/max does not order welded indices - split-seam edges keyed twice, drawn twice, never culled), showNET discovery no longer hot-spins on repeated SocketExceptions and reuses its receive buffer, oversized showNET frames warn on the plug status instead of silently blanking, and the new input entries sit at their sorted .t3ui positions. |
| `feat: laser safety masks, rendered safety shapes, per-channel color levels` | From the MadLaser guide's feature list: quads on the laser output card that blank the beam inside (or dim it to a level for reduced-level audience scanning) or allow only inside, edited like the keystone corners and applied in the safety stage after the warp so no graph content bypasses them; **Render shapes on the laser** streams the safe zone and mask outlines faintly onto the projection for aiming; **Color gains / min** calibrate each diode per channel (lifting weak diodes, cutting the near-black glow). Manual test steps and 8 new safety-transform/settings tests. |
| `feat: laser per-channel color time shift` | Output Setup **Color shift (R G B)** (up to 32 points, negative leads the beam) moves each diode's color along the path for diodes that react at different speeds. It runs right after the scan-field clamp, before the masks and the brightness ceiling, and never lights a blanked point, so the safety stages still hold. Also fixes the color-level stage clamping full-scale colors to 32767 instead of 65535 (any non-neutral gain halved full white). Manual test step and 9 new tests. |

Two reference branches were also created locally (not merged, not pushed):
`archive/devdevdev-allin-wip` and `archive/devdevdev-stable-auto-startup-dmx` (see "Not merged").

## Source audit — what came from where

The three folders contain one evolving line, not three independent ones:

```
DEVDEVDEV  (Oct 25 → Nov 2)   allin-* clones → tixl-multi-allin (+ worktree tixl-stable-auto-port)
   ↓ re-split into feature branches
TiXLDEV    (Nov 7 → Nov 9)    new-multi-tixl  (19 feature branches + 13 worktrees)
   ↓ same branches continued + merged
CrossTixlDEV (Nov 9 → Nov 12) tixl: main-collective  (+ worktree tixl-aimodels)
```

Every branch and every working tree was compared file-by-file (blob hashes) against main-collective's full history: a file counts as *superseded* when main-collective once contained exactly that content and later evolved it.

| Folder / repo | Branches & working trees checked | Result |
|---|---|---|
| `CrossTixlDEV\tixl` | 20 local branches, working tree (clean) | **Merged** — `main-collective` contains all 19 feature branches; only `pr-evidence` (PR screenshots) is not in it, intentionally |
| `CrossTixlDEV\tixl-aimodels` | worktree of `feat/ai-model-manager` | Merged (branch is in main-collective, no uncommitted source) |
| `TiXLDEV\new-multi-tixl` | 30+ branches, main working tree, 13 `.worktrees/*` | **Superseded**. Uncommitted StreamDiffusion/CUDA edits and the `laser-hardening` worktree edits (E-stop latch, SubmitBlank, PONK NaN guard…) are all already in main-collective. `backup/collab-int-flavor` and `backup/omt-workspace` are older drafts of features main-collective re-did |
| `TiXLDEV\tixl-new`, `tixl-repo/*`, `tixl` | Sep-2026 branches (MeshToSDF, MadMapperMaterials, DepthAnything v1, StreamDiffusion v1, pinpad-lock) | Older than 2 months; MeshToSDF/DepthAnything/StreamDiffusion exist in far newer form. Not merged |
| `DEVDEVDEV\tixl-multi-allin` | 50+ branches (`category/*`, `int/all`, `rerun/*`, `agent-*`, `tmp/*`), working tree | Superseded **except**: 15 uncommitted WIP files (collab/failover/MeshToSDF/RemoteVideoInputBus) and the DA3 doc screenshots → see "Not merged" / docs commit |
| `DEVDEVDEV\tixl-stable-auto-port` | worktree of `stable/auto-startup-dmx` (8 commits, Oct 31) | **Partly ported** (startup project / autostart / crash-watchdog were already in main-collective; the rest ported — see below) |
| `DEVDEVDEV\allin-*` | 7 clones of tixl-multi-allin (Oct 25) | Fully superseded (`allin-linux-audit-broken` has a dangling alternates path and cannot be opened) |

## Features on `crosstixlultimate` (all via main-collective)

| Feature branch (origin) | Last change | Content |
|---|---|---|
| `feat/collab`, `feat/collab-hardening` | 11-11 | Live collaboration (LAN/online, Discord login, relay, WebRTC, lobby, chat, remote tab, spectator), hardening |
| `feat/failsafe-boygrouping-collab` | 11-10 | Boygrouping, show failover, NTP/PTP clock sync, adapter selection |
| `feat/laser-support` | 11-10 | Laser output (EtherDream/PONK/CITP), LaserCamera, MeshToLaserPoints, Output Setup laser routing, E-stop latch |
| `feat/nanovdb` | 11-09 | OpenVDB/NanoVDB volume ops (28 ops) + 26 examples |
| `feat/bepu` | 11-09 | Bepu rigid-body physics (6 ops) + 25 examples |
| `feat/assimp` | 11-09 | AssimpImport / AssimpExport, isolated import host |
| `feature/omt` | 11-09 | OmtInput / OmtOutput (Vendor/libomtnet) |
| `fix/mediapipe` | 11-09 | MediaPipe operator fixes + examples |
| `feat/depthanythingv3` | 11-09 | Depth Anything 3 + DepthFilter/Threshold/ToNormal/ToPoints/ToPosition/Parallax |
| `feat/streamdiffusionv2` | 11-09 | StreamDiffusion V1/V2 (Wan2.1 DiT on ONNX Runtime, TensorRT) |
| `feat/meshtosdf`, `feat/csvtopoints` | 11-09 | MeshToSDF, SdfPointsToGeometry, CsvToPoints |
| `feat/ai-model-manager` | 11-11 | AI model manager |
| `feat/qualityoflife` | 11-10 | Editor quality-of-life, startup project/autostart/crash watchdog, webcam camera controls |
| `fix/videodeviceinput`, `fix/videostreaminput-rtmp` | 11-09 | Capture and RTMP fixes |

Supporting changes outside `Operators/` come along with these: `Core/` (73 files: Show/, Video/, Volume data types, laser output, ONNX/CUDA selectors), `Editor/` (116: Collab/, Show/, Output Setup, Hub), `Graphics.*`, `VideoServices`, `SdlPlatform`, `Vendor/libomtnet`, `CollabRelay`, `CollabBackend`, test projects, `.help/` and `docs/new-operators-2026-11/`.

### Ported from `D:\DEVDEVDEV` `stable/auto-startup-dmx` (not previously in main-collective)

- **StructuredBufferReadAccess** — non-blocking readback with retry instead of blocking on the head request; 8 staging slots; failures dropped instead of thrown into the frame loop. Rewritten from SharpDX onto `Graphics.Compat` (`TryMapSubresourceForRead`: D3D11 DoNotWait / Vulkan fence check).
- **OpTimings** — opt-in per-operator timing via debug bridge `getOpTimings` / `setOpTimings` (documented in `.agentic/DEBUG_PROTOCOL.md`).
- **[PointsToDmxLights]** — once-per-frame evaluation, guarded recompute, warn once per occurrence, stale output cleared on disconnect, fewer allocations. ⚠ **Behavior decision:** `FixturesPerUniverse` now uses the stable line's semantics — a batch may span several universes (e.g. 600 RGB pixels → 4 universes), the next batch starts on a fresh universe. main-collective had capped a batch to one universe; that cap is removed. Docs and tooltip updated.
- **[ArtnetOutput]** — async lock-protected adapter refresh, inputs read once, list reuse; main-collective's `LocalAdapterResolver` and universe-overlap resolution are kept.

### Operator tables

#### Added operators (vs tixl3d/tixl main)

| Package | Folder | Operators |
|---|---|---|
| DepthAnything | Lib/io/video/depthanything | DepthAnything, DepthFilter, DepthParallax, DepthThreshold, DepthToNormal, DepthToPoints, DepthToPosition |
| Examples | Lib/field/generate/sdf | MeshToSDFExample |
| Examples | Lib/io/ai | StreamDiffusionExample, StreamDiffusionExampleImg2ImgRestyle, StreamDiffusionExampleV2Restyle |
| Examples | Lib/io/video/depthanything | DepthAnythingExample, DepthAnythingExampleNormalSurface, DepthAnythingExampleParallaxMove, DepthAnythingLiveExample, DepthAnythingSplatExample |
| Examples | Lib/io/video/mediapipe | FaceDetectionExample, FaceLandmarkDetectionExample, GestureRecognitionExample, HandLandmarkDetectionExample, ImageSegmentationExample, MediaPipeExample, ObjectDetectionExample, PoseLandmarkDetectionExample |
| Examples | Lib/mesh/generate | AssimpImportExample |
| Examples | Lib/physics | 25 examples: MeshPhysics, BouncingBalls, BrickWall, CapsulesAndCylinders, Car, Contacts, DominoRun, Fracture, Motor, Net, NewtonsCradle, Particles, Planet, Ragdoll, RopeBridge, Rope, SdfField, Seesaw, ToVolume, VdbCollider, VolumeCollider, VolumeCsg, VolumeGravity, Vortex, WeldedPairs |
| Examples | Lib/point/io | CsvToPointsExample |
| Examples | Lib/volume | 26 volume/NanoVDB examples (DrawVdbBounds … VolumeVdbFiles) |
| Lib | field/generate/sdf | MeshToSDF |
| Lib | geometry | SdfPointsToGeometry |
| Lib | io/file · mesh/generate | AssimpExport · AssimpImport |
| Lib | laser/* | LaserCamera, LaserPointsToBuffer, MeshToLaserPoints, PONKInput |
| Lib | physics | PhysicsWorld, RigidBody, RigidBodyGeometry, RigidBodyPoints, RigidForce, RigidJoint |
| Lib | point/io | CsvToPoints |
| Lib | volume/* | VolumeInfo, DrawVdbBounds, NanoVdbToPoints, VolumeToField, VolumeToMesh, VolumeToPoints, VolumeToTexture3D, VdbDensityField, VdbToSdfField, FieldToVolume, VolumeFromMesh, VolumeFromPoints, VolumePrimitive, VolumeToNanoVdb, LoadNanoVdb, LoadVdb, SaveNanoVdb, SaveVdb, VolumeCsg, VolumeFilter, VolumeMath, VolumeMorphology, VolumeNoise, VolumeResample, NanoVdbRender, VolumeField |
| Omt | Lib/io/video | OmtInput, OmtOutput |
| StreamDiffusion | Lib/io/ai | StreamDiffusion |
| Video | Lib/io/video | VideoDeviceInput (new Guid, see fixes) |

#### Modified operators

| Package | Folder | Operators | Change |
|---|---|---|---|
| Lib | point/draw, mesh/draw, render/_dx11/api | Draw, DrawInstancedIndirect, 14 point-draw and 8 mesh-draw ops | `[Draw]` geometry-capture tagging (CaptureKind / CaptureVertices) |
| Lib | io/video | WebcamInput | Camera controls, capture hardening, hot-plug |
| Mediapipe | Lib/io/video/mediapipe | 7 detectors | MediaPipe fixes |
| Video | Lib/io/video | VideoStreamInput | RTMP fixes |
| Examples | user/pixtur/vj/scenes | VJSkullScene | asset path |
| Io | Lib/io/dmx, midi, osc | ArtnetOutput, DmxOutput, PointsToDmxLights, SacnInput, SacnOutput, Video2DPointScanner, MidiInput, OscInput | C# only: adapter binding, failover-standby silence, boygroup mirroring, DMX port |
| Lib | io/ptz, io/video, render | OnvifCamera, CameraCalibrator, SetPixelAndVertexShaderStage, _DispatchSceneDraws | C# only |

## Upstream conflicts resolved (merge onto `3459907`)

Upstream added 71 commits since main-collective's base (`5bc0c54`): render-pass state refactor, velocity buffers, resave of all ops, output-setup work. 35 files were changed on both sides; 32 merged automatically.

| File | Resolution |
|---|---|
| `Editor/App/DebugProtocol/DebugServer.cs` | Kept both: upstream's `TryReadOptionalGuid` and main-collective's `addOp` reference-cycle guards (`SymbolContains`) |
| `Operators/Examples/Symbols/testing/AllTests.t3ui` | Upstream (its resave removes the obsolete layout `Settings` block on purpose) |
| `Operators/Lib/Symbols/point/draw/DrawLines.t3` | Upstream rebuilt the graph with new child ids (velocity/normal buffers). Took upstream and re-applied main-collective's capture tagging to the new `[Draw]` child (`fbad3319…`: CaptureKind = 2, GPoints → CaptureVertices) |

The new upstream op `[DrawLinesShaded]` does not have the capture tagging yet (same one-line change if wanted).

## Fixes to problems that already existed on main-collective

- `t3.sln` was missing `Operators/StreamDiffusion` and `StreamDiffusion.Tests`, so a Release solution build failed when the Editor packaged StreamDiffusion. Both are added back.
- Parallel solution builds could package the Editor before an operator package had finished (`Operator package Lib has no Release build output`). `t3.sln` now declares the Editor's build dependency on all 13 packaged operator projects.
- Duplicate `PackageReference`s (NU1504): `DataChannelDotnet` (Editor), `AssimpNetter` (Lib); duplicate `DepthAnything` package entry in Editor packaging.
- `Operators/Video/.../VideoDeviceInput` had the **same symbol Guid** (`cd5a182e…`) as Lib's `[WebcamInput]` (upstream renamed VideoDeviceInput → WebcamInput), and its `.t3` had been deleted by a merge. Now it has its own Guid `34c2d58d-ae76-47b6-9713-02911a783341` and its `.t3` is restored from `0ae1d20` (23 inputs match `.cs` and `.t3ui`). Existing graphs keep resolving `cd5a182e` to WebcamInput. It is kept because `Operators/Video/Utils/PeerCameraProvider.cs` depends on it.

## Integrity checks (whole repo, 1619 `.t3`)

| Check | Result |
|---|---|
| `.t3` ↔ `.t3ui` ↔ `.cs` triples | OK. Only 4 `.cs` missing, all already missing in upstream: `DrawPointsExample`, `AlexLineTest`, `APoint`, `I02p_AnalyzeImageLevels` |
| Orphan `.t3ui` | 0 (was 1: VideoDeviceInput, fixed) |
| Duplicate symbol ids | 1, from upstream: `APoint` / `Types.Point` share `9989f539…` |
| `.cs` `[Guid]` = `.t3` id | All match |
| Duplicate `.cs` Guids | 0 (was 1, fixed) |
| Child `SymbolId` references | All resolve except 1 already broken upstream (`AlexLineTest`) |
| Asset paths (`Pkg:path.hlsl/.png/…`) | Same set of missing paths as upstream (19, all pre-existing) |
| Case-only path collisions (Windows) | 0 |

## Tests (Linux, Release)

| Suite | Result | Notes |
|---|---|---|
| Lib.Tests | 129/129 ✅ | |
| VideoServices.Tests | 78/78 ✅ | |
| Mediapipe.Tests | 29/29 ✅ | |
| CollabHeadless harness | ALL PASSED ✅ | collab, deploy, boygroup, failover election |
| StreamDiffusion.Tests | 99/100 | `FeedbackBlendTests.HistoryIsTheBlendedFrame_TrailsDecay` fails on main-collective too |
| Core.Tests | 536/582 | 43 `NativeAudioEngineTests` (native audio library not present in the Linux test host) and 2 `ImGuiLayoutScalingTests` fail on main-collective too. 1 new: `CompositionSettingsProxyTests.NonDefaultProxy_PersistsEvenWhenNotEnabled`. Upstream changed `CompositionSettings.WriteToJson` to drop disabled settings on purpose and left this test stale, so upstream main fails it as well |

**Not verified here:** this machine cannot start the Windows (D3D11) editor. Before pushing, run a Windows `dotnet build t3.sln -c Release` and a first editor start in `D:\CrossTixlClaude`.

## Not merged (kept for reference only)

| Item | Why | Where it is now |
|---|---|---|
| DEVDEVDEV `tixl-multi-allin` uncommitted WIP (15 files: Nov-2 collab transport/relay/protocol/manager, 1926-line FailoverArbiter with 1:1 mirror, FailoverHealthLink/CollabLink/Mirror, PeerCameraService, RemoteVideoInputBus retire/purge, TransformGizmo overlay fix, MeshToSDF `Padding4`) | Targets the pre-redesign Nov-2 collab/failover line. main-collective rebuilt collab/failover after that, and dropped those four Show files at `441c746` because they "were never referenced and never compiled". Porting means reworking the redesign, not merging | `archive/devdevdev-allin-wip` (together with tip `c35192f`; the two DA3 `.onnx` models of 207 MB and 100 MB are left out) |
| DEVDEVDEV `stable/auto-startup-dmx` original commits | Ported (see above) | `archive/devdevdev-stable-auto-startup-dmx` |
| DA3 ONNX models in DEVDEVDEV history | Too large for GitHub (100 MB limit); downloaded by `Operators/DepthAnything/Scripts/download_models.*` | Still in `D:\DEVDEVDEV\tixl-multi-allin\.git` |
| Sep-2026 TiXLDEV branches (MadMapperMaterials, pinpad-lock, v1 DepthAnything/StreamDiffusion) | Old, never reached the Nov lines | In their repos' `.git` (kept by cleanup) |
| `CrossTixlDEV` `pr-evidence` | PR screenshots only | `origin/pr-evidence` |

## Housekeeping notes

- Stray files at repo root came in from main-collective: `camera-dshow-fix.png`, `collab-leave-latch.png`, `collab-presence-chat.png`, `offset.txt`, `tixl-linux-gpu.patch`. They are left untouched; they could be moved to `docs/` or removed.
- `D:\CrossTixlClaude` remotes: `origin` (banidu-512/tixl), `upstream` (tixl3d/tixl). The temporary local remotes used for the audit were removed.
