# Plans Overview

Status of every plan in this folder, triaged **2026-10-31** (`feat/linux-port` 811eec982, `main` 8fac5116b).

**How this was checked:** each plan was read and compared with the code on both branches using grep and git
history. Nothing was built or run, so "shipped" means the code exists, not that it works, and "absent" means
the plan's names were not found. GitHub tickets and manual-test results were not checked. Plan headers were
not trusted, because many are stale.

## At a Glance

64 active documents: 53 plans here (one, `Plan_StructuredData`, on `main` only) and 11 in `output-setup/`. Another 20 were
[cleared out](#archived-in-this-cleanup) in the first cleanup.

| Bucket | Count | What to do |
|---|---:|---|
| [Nearly done](#nearly-done) | 5 | Close the small items or move them, then archive |
| [Loose ends](#loose-ends) | 22 | Most shipped; rewrite the status as "what's left" |
| [Active](#active) | 4 | In progress |
| [Ready](#ready) | 15 | Not started or barely started, and still valid |
| [Parked](#parked) | 9 | Keep; no work scheduled |
| [Output Setup docs](#output-setup) | 9 | 6 design references, the index, the backlog, 1 historical spec |

## Worth Your Attention

These matter more than any filing decision.

**Confirmed in the code**
- **CI runs no tests.** There is no `dotnet test` under `.github` on either branch, though three xUnit projects
  exist. This is the cheapest high-value step in `Plan_AutomaticTests`.
- **The stall overlay is off under Vulkan.** `Editor/App/StallWatchdog.cs:45` returns early, so on the Linux
  branch it only runs with `TIXL_BACKEND=d3d11` on Windows. `Plan_GraphicsFacade` decision 7 says the opposite.

**Found by reading, not run; each needs a quick test**
- **Linked assets on export may break.** `PlayerExporter.ExportInfo.cs:114` computes a path relative to the
  package root, which looks wrong for a linked asset outside it.
- **Audio FX on Linux.** The FX ops call `Bass.FX*` with no OS gate, while `Plan_ReplaceBass` says DX8 effects
  are Windows-only.
- **SharpDX.WIC on Linux.** `Help/VideoThumbnails.cs:4` and `Core/DataTypes/Texture.cs:10` use it with no OS gate
  found.
- **One failing op may crash the editor on macOS.** `OutputUi.cs` has no catch; upstream catches were not traced.
- **Fracture quality.** The last result in `Plan_ProceduralGeometry` shows failures (ring 6/6, sweep 24/36, up to
  6% volume loss), and the later fracture commits found only fix build warnings.
- **glTF Phase 5 has no recorded test.** `SkinPoints`, `BindToSkeleton` and the other Phase 5 ops shipped in v4.3
  still labelled "untested in editor".

**Features that exist but cannot be reached**
- `CreateFromTemplateDialog` is drawn every frame, but nothing opens it (`Plan_NewSymbolAndImport`).
- `EvaluateProxyNeed` has no callers, so proxy auto-trigger is dead code (`Plan_VideoProxyMedia`).
- `Slice` rotation is stored but never edited or rendered (`output-setup/long-term-features.md`).

**Time-sensitive**
- **macOS settings folder.** `Plan_MacOS` said to decide before the first public build, and signed DMG builds
  already exist.
- **4.3 frame-timing baseline.** `Plan_FrameTiming` asks to record it, and the 4.3 notes landed on 2026-10-29.
- **Audio graph vs. BASS.** Decide remaining `Plan_AudioProcessingGraph` items against `Plan_ReplaceBass` first,
  since that plan drops effects and reroutes spatial audio. Otherwise the work may be done twice.

## Decisions for You

1. **Video transitions have two conflicting designs.** `Plan_StreamlineEditing` §3 treats an overlap as a
   transition; `Plan_TimelineClipUx` Phase D places an explicit clip. Recommendation: settle this before Phase D.
2. **Time inside an entered clip also has two designs.** `Plan_TimeClipSourceExtent` Part 3 remaps on entry;
   `Plan_TimelineClipUx` A2 uses a persisted toggle. Recommendation: merge them into one design.
3. **Reference points were overtaken by the code.** `Plan_StageExtension` §5 and
   `output-setup/camera-calibration.md` want a setup-level entity, but owner-local annotations shipped. Moving
   now needs a migration. Recommendation: re-decide when that work is picked up, not before.
4. **`Plan_CrossPlatformV5` no longer describes reality.** Its "v5, Vulkan only" framing conflicts with v4.4 tags and
   D3D11 still being present. Recommendation: rewrite it as one "merge to main, then what remains" tracker.

## Nearly Done

| Plan | Remaining | Branch |
|---|---|---|
| [Plan_VariationPicker](Plan_VariationPicker.md) | Phases 1-5 shipped, though 4-5 are unmarked. Left: inline fader view, whether `PopupId` const collides between two pickers. Its "order comes from canvas position" text is wrong | both |
| [Plan_HelpWindow](Plan_HelpWindow.md) | Learn tab was removed (c92ef5ba1), so drop Phase 3. `.tests-manual/help-window.md` still tests it and would fail. Left: component highlighting, usage ranking | both |
| [Plan_MidiDeviceAliases](Plan_MidiDeviceAliases.md) | "Export alias file" button; hand tests for Use Alias and Player override. Code is on both, plan file on linux only | both |
| [Plan_FlipMidiControllerLedLayout](Plan_FlipMidiControllerLedLayout.md) | APC40 Mk1/Mk2 and hardware confirmation. The commit did more than the plan says | both |
| [tixl-debug-protocol-plan](tixl-debug-protocol-plan.md) | `DEBUG_PROTOCOL.md` is now canonical. Move leak test, CI and recorder to AutomaticTests. Method sets differ per branch | both |

## Loose Ends

### Video and Audio

| Plan | Shipped | Open |
|---|---|---|
| [Plan_AudioProcessingGraph](Plan_AudioProcessingGraph.md) | Buses, FX ops, ducking, migration, Player | `[BlendAudio]`/`[MixAudio]`, Direct/spatial routing, multi-send, reference lines, retiring `Playback.AudioClips` and `IsMainSoundtrack`, kill-all. |
| [Plan_VideoAudio](Plan_VideoAudio.md) | Decoding, playback, export | Phase 6 (`atempo`, scrub, track selection), A/V offset measurement, missing-texture-path warning, live check of loop seams and export |
| [Plan_VideoClipPlayer](Plan_VideoClipPlayer.md) | Player, clips, AutoCollect, stream limit | Wired-set invalidation limit, descriptor pool and direction-aware preroll, efficiency test set, orphan `PlayVideoClip.md`. Could merge into VideoFollowups |
| [Plan_VideoFollowups](Plan_VideoFollowups.md) | BT.709 tags, global cache budget (still listed open in the plan) | Off-thread encode, `hvc1`, DNxHR, GIF, webm, reverse read-behind, keyed-mutex fallback, moving `SwiftCamDevice`, project migration for the Video package |
| [Plan_VideoProxyMedia](Plan_VideoProxyMedia.md) | Transcoder, eligibility, resolve, settings | Phase 3c trigger, Phase 4 render toggle, staleness, LRU budget, test set, playback tearing check |
| [Plan_ContinuousCapture](Plan_ContinuousCapture.md) | Capture, clock, docs, test set | VFR, realtime audio, fragmented MP4 for long captures, mid-capture resize |

### Editor and Timeline

| Plan | Shipped | Open |
|---|---|---|
| [Plan_TimeClipEvaluation](Plan_TimeClipEvaluation.md) | Phases 1-2 | Phases 3-4 (`TimeSpace`, `SourceRangeBehaviour`) and test sets; release note for the changed clip-local shift and slip direction |
| [Plan_TimeClipSourceExtent](Plan_TimeClipSourceExtent.md) | Parts 1-2, `TimeClipPlayer` | Part 3 awaits [decision 2](#decisions-for-you) |
| [Plan_TimelineClipUx](Plan_TimelineClipUx.md) | A1, Phase B, single-output VideoClip | A2, Phase C, Phase D, mode-switch bug (`TimeControls.cs:566`), raw-time holdouts, Remove Unused Keyframes. Many open questions |
| [Plan_Sections](Plan_Sections.md) | Phases 1-3 | Phase 4 clamped titles (small, visible win); Phase 5 background image and member order |
| [Plan_BlendSnapshotsOp](Plan_BlendSnapshotsOp.md) | Level A and A0 | Levels B and C (Player support), name dropdown, Auto indicator, undo spam |
| [Plan_ThumbnailRendererExtraction](Plan_ThumbnailRendererExtraction.md) | Phase 1 of 3 | Canvas-loop migration, cleanup and test set. Do together with the picker contention |
| [Plan_UndoRedoCoverage](Plan_UndoRedoCoverage.md) | Most commands | #6 residual, #12 (`TimeControls.cs:393` still mutates BPM directly), #13, #15, #17, #20. Re-audit; many references are stale |
| [Plan_BlockingJobOverlay](Plan_BlockingJobOverlay.md) | Watchdog, overlay, scopes | Vulkan/Linux path, kill switch, input queued during a stall, more scopes |

### Rendering, Assets and Export

| Plan | Shipped | Open |
|---|---|---|
| [Plan_ProceduralGeometry](Plan_ProceduralGeometry.md) | 28 ops, data types, v4.3 notes | Missing ops (`SelectGeometry`, `CombineGeometry`, `SetGeometryAttribute`, curve ops, ...), fracture defects, static `_lengthList`, font licence, docs, demo. Phases 6-7+ unstarted. 992 lines; split it |
| [Plan_GltfAnimation](Plan_GltfAnimation.md) | All 11 ops, docs, test set | Phase 5 editor test and verification items 3, 4, 6, 7; no shipped example uses these ops |
| [Plan_LinkedAssetFolders](Plan_LinkedAssetFolders.md) | Links, import dialog, relink | Collect assets on export (see above) |
| [Plan_PlayerExportCleanup](Plan_PlayerExportCleanup.md) | Dialog, stripping, cache pruning, load report | Background image; loading screen is Windows-only (Direct2D); open questions 3-6 |
| [Plan_BuildOutputClean](Plan_BuildOutputClean.md) | Release-only clean, startup addendum | Trim to the rest: mirror copy with `SkipUnchangedFiles` (largest win), stamp, editor-requested clean |
| [Plan_BrokenPackageRecovery](Plan_BrokenPackageRecovery.md) | Broken-project capture, restore dialog, missing items | Dependency cascade, `Replace with...`, reopening from Hub, failure categories, TOOLL3-YE rejection, test set, in-place restore |

### Platform

| Plan | Shipped | Open |
|---|---|---|
| [Plan_LinuxEditor](Plan_LinuxEditor.md) | M1-M3 | M4 SharpDX.WIC, M5 second-compositor test set, vertex semantics, SilkWindows vs. SDL. Then archive |
| [Plan_GraphicsFacade](Plan_GraphicsFacade.md) | All four projects, migration, queries | Sub-allocator, pipeline cache, D3DCompiler/WIC in Core, stale header. Keep decisions as reference |

## Active

| Plan | State | Next |
|---|---|---|
| [Plan_KeyboardControl](Plan_KeyboardControl.md) | Navigation, popover, toasts, bookmarks and peek shipped, on both branches | Scoped keymap and hint bar, key-convention cleanup, slider ×10 still on Alt, multi-select, toast mute UI, toast/peek test set. |
| [Plan_FrameTiming](Plan_FrameTiming.md) | Last commit 2026-10-22; branches diverge (linux deleted the estimator) | `PerformanceMetrics` window, hard-coded 60s, MediaPipe `33333`, NDI timecode, blocking export readback, 4.3 baseline. §A4 contradicts the decision to use SDL's display mode |
| [Plan_CrossPlatformV5](Plan_CrossPlatformV5.md) | Foundation shipped on linux, up to v4.4.0.4 tags; none on `main` | Merge, sub-allocator, pipeline cache, SharpDX removal, `[SupportedPlatforms]`, tests in CI, Silk. See [decision 4](#decisions-for-you) |
| [Plan_MacOS](Plan_MacOS.md) | Portability, CoreMIDI, DMG, CI shipped on linux | Visual suite (71/97 on 2026-10-08), cube placeholder, line-only OBJ, player `.app`, FFmpeg in DMG, settings folder. Cannot be verified from Linux |

## Ready

| Plan | State | Note |
|---|---|---|
| [Plan_AutomaticTests](Plan_AutomaticTests.md) | Some tests exist; runner phases don't | Start with CI. "No test projects" is false |
| [Plan_CleanInstallLayout](Plan_CleanInstallLayout.md) | Nothing shipped | Do before InstallVerification and WindowsSigning: manifest paths and the signed exe depend on it |
| [Plan_InstallVerificationAndSafeStartup](Plan_InstallVerificationAndSafeStartup.md) | Phase 1 shipped | Only one of the two resolve hooks registered; `--safe` absent |
| [Plan_WindowsSigning](Plan_WindowsSigning.md) | Unsigned workflow, linux only | First step is an Azure account task for you |
| [Plan_ReplaceBass](Plan_ReplaceBass.md) | Spike on `spike/replace-bass` only | `Spikes/MiniaudioSpike` isn't on this branch, so its `dotnet run` fails here. Licensing drives priority |
| `Plan_StructuredData` | Nothing shipped; **plan on `main` only** | Open question 1 waits for you. Sequence with geometry attributes. Fix path case `Operators/examples` |
| [Plan_SceneDocument](Plan_SceneDocument.md) | Nothing shipped | Owns the skinning contract; remove the duplicates from ProceduralGeometry Phase 7 |
| [Plan_SlotUpdateActionCleanup](Plan_SlotUpdateActionCleanup.md) | Point patches only | Its gate ("after 4.3 timeline work") appears met |
| [Plan_RuntimeConsistencyRecovery](Plan_RuntimeConsistencyRecovery.md) | Nothing shipped | Update for `RestoreBackupDialog` and Vulkan device loss. Share one recovery dialog with InstallVerification and BrokenPackageRecovery |
| [share-project-and-packages-plan](share-project-and-packages-plan.md) | Phase 1 shipped; Phase 2 built under BrokenPackageRecovery | Reduce to Import and Feed |
| [Plan_NewSymbolAndImport](Plan_NewSymbolAndImport.md) | Nothing shipped | New Symbol is cheap; Import needs design. Overlaps share-project |
| [Plan_CreateSnapshotFromEmptySlot](Plan_CreateSnapshotFromEmptySlot.md) | Nothing shipped | Small; 4 interaction decisions |
| [Plan_ReferenceLines](Plan_ReferenceLines.md) | Nothing shipped | `AutoCollectIndicators` covers part of it; decide whether to absorb |
| [Plan_RenameSimulateIoDataToDataClipPlayer](Plan_RenameSimulateIoDataToDataClipPlayer.md) | Nothing shipped | Op path in the plan is stale; two help pages also need updating |
| [Plan_UiTimelapse](Plan_UiTimelapse.md) | Nothing shipped | Its encoder gate looks set to disable it on the Radeon machine, since the probe has no VAAPI candidate (not run). Cursor code paths are stale |

## Parked

| Plan | Why parked |
|---|---|
| [Plan_DependencyHealth](Plan_DependencyHealth.md) | Reference and decisions. FFmpeg.AutoGen migration trigger ("port and installer done") is getting close. [dependency-age.py](dependency-age.py) works and locates the repo from its own path, so move it deliberately |
| [Plan_SlangInProcess](Plan_SlangInProcess.md) | Deferred by its own decision. Supersedes the COM API line in CrossPlatformV5 |
| [Plan_MidiControllerAbstraction](Plan_MidiControllerAbstraction.md) | Phases 2-4 sequenced after the port; Phase 1 superseded by aliases. Refresh the audit and RtMidi sections first |
| [Plan_StreamlineEditing](Plan_StreamlineEditing.md) | Roadmap umbrella. Audio status is stale; see [decision 1](#decisions-for-you) |
| [Plan_StageExtension](Plan_StageExtension.md) | Direction notes; see [decision 3](#decisions-for-you) |
| [Plan_ImageComposeTransform](Plan_ImageComposeTransform.md) | Nothing shipped. The name `TransformImage` is already taken by an op with different semantics |
| [Plan_VideoHdrExport](Plan_VideoHdrExport.md) | Polish; needs an HDR display |
| [output-setup/multi-machine](output-setup/multi-machine.md) and [camera-calibration](output-setup/camera-calibration.md) | Pixel streaming shipped; machine id, heartbeat and calibration not. Calibrate tab is gone, so §6 needs rethinking |

## Output Setup

Phases A-C, the refactoring programme and Player export shipped on both branches.
`origin/feat/projection-mapping` is an ancestor of both, so it holds nothing unique. The UI mirror
(`EditorUiContent.cs`) is linux-only. Newer work (`FloorPlan`, `StageGeometry`, `ProjectOntoStage`,
`WallpaperLayout`, `OutputCompositor`) has almost no plan coverage.

**Design references, which still need an "as-built differs" note:**
[data-model](output-setup/data-model.md), [selection](output-setup/selection.md),
[states](output-setup/states.md), [canvas-interaction](output-setup/canvas-interaction.md),
[binding-examples](output-setup/binding-examples.md), [use-case-flows](output-setup/use-case-flows.md).

[`output-setup/long-term-features.md`](output-setup/long-term-features.md) is the single backlog, and
[`output-setup/README.md`](output-setup/README.md) is the index and glossary.
[`output-setup/output-settings-spec.md`](output-setup/output-settings-spec.md) is historical, kept because other docs cite its
callout numbers.

**Most misleading statements in the design docs**
- "Name-first, index-fallback" binding: `Core/Output/PlugBinding.cs:34-38` resolves by `DisplayIndex` alone.
- `data-model` §1 cites `SetupPanel`, `OutputManager`, and `ReferenceImageView`, all gone or changed.
- `selection.md` calls the sub-element plane shared; it is a private per-view `SelectionSet`.

## Housekeeping

**Branches.** These plans differ between `feat/linux-port` and `main`, so archive moves need to be cherry-picked or
repeated with that in mind.
- Linux only: `Plan_CleanInstallLayout`, `Plan_DependencyHealth`, `Plan_LinuxEditor`, `Plan_MacOS`,
  `Plan_MidiDeviceAliases`, `Plan_SlangInProcess`, `Plan_WindowsSigning`, `dependency-age.py`.
- Main only: `Plan_StructuredData`.
- `main`'s `Plan_CrossPlatformV5` is the stale 09-11 draft ("Nothing implemented yet").

**Unowned leftovers from `Plan_ShaderGraphBypass`:** `_` internal ops may still be bypassable, and the v4.3 notes don't warn
that saved projects can look different after the bypass fix.

**Plan references in code, against the comment rule:** `Graphics.Compat/Descriptions.cs:11`,
`Graphics.Compat/Enums.Generated.cs:2`, `Graphics/Formats.Generated.cs:2`, and the generator that writes them,
`Spikes/GraphicsEnumGen/Program.cs:25`.

**Stale comments and possibly unused code in Output Setup:** `SetupFlowOutliner.cs:27` mentions a removed shelf,
`SetupParameterView.cs:152` mentions `SendToOutput.TargetIds`, and `ProjectorSolver` has only test callers in the
paths searched.

## Archived in This Cleanup

**Moved to `archive/`:** `Plan_AudioClipPlayer`, `Plan_DragFromInputSlot`, `Plan_DragFromMultipleOutputs`,
`Plan_GetVarHoverLink`, `Plan_MidiFileImport`, `Plan_MultiViewport`, `Plan_PreUpgradeBackup`, `Plan_ShaderGraphBypass`,
`Plan_SnapshotControlView`, `Plan_TimelineAudioClips`, `Plan_VideoDeviceInput`, `move-build-output-to-temp-plan`,
`tixl-debug-protocol-audit`.

**Moved to `archive/output-setup/`:** `anchor-yup-pass`, `implementation-plan`, `refactoring-plan`,
`shared-selection-slice`, `straighten-slice`, `ui-restructuring-plan`.

**Deleted:** the top-level `Plan_FfmpegEncode` draft. The later version is in `archive/`.
