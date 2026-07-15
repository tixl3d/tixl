# Streamline editing (NLE-style video + audio) — umbrella

The original goal was broad: make TiXL's timeline support non-linear video + audio editing like **Audacity** and **Final Cut Pro**. The audio thread grew into its own sub-project (three detailed plans below); this doc frames the whole initiative and **captures the other workstreams' findings** — several were designed in discussion but not yet written down. Each "needs its own plan" section is a design summary to expand later, not a throwaway note.

## Workstreams

### 1. Audio processing graph — [`Plan_AudioProcessingGraph.md`](Plan_AudioProcessingGraph.md) (detailed)
Audio clips become reference-emitting operators collected by a per-media root (`GenerateAudioProcessingBlock` ≈ `GenerateShaderGraphCode`), generalizing the field/shader-graph pattern. Currently being de-risked via a BASS-routing spike. Supersedes the "audio clips as data" direction of `Plan_TimelineAudioClips.md`.

### 2. Reference lines — [`Plan_ReferenceLines.md`](Plan_ReferenceLines.md) (detailed)
Implicit/wireless relationships (Set/Get vars, audio auto-collection, future Send/Receive) rendered as dashed lines, realizable into wires. Generalizes the existing hover-only `OpUi.DrawVariableReferences` / #1077.

### 3. Video transitions — *needs its own plan*
**Primitives already exist.** [`BlendImages`](../../Operators/Lib/image/use/BlendImages.cs) is the natural cross-fade (a `BlendFraction` float + `MultiInputSlot<Texture2D>`); also `Blend`, `BlendScenes`, `BlendWithMask`, obsolete `TriangleGridTransition`. [`VideoClip`](../../Operators/Video/lib/io/video/VideoClip.cs) already carries per-clip `Color` (tint+alpha) and `BlendMode`. The `_ProcessVideoClips` compositor stacks active clips with opacity/blend but has **no cross-fade between adjacent clips**; it already classifies clips `Active`/`Upcoming`/`Inactive` around cut boundaries.

**Decisions reached:**
- **Overlap = transition** (FCP geometric model), *not* attached transition objects (Premiere model). Two clips overlapping on the same layer *are* a transition; the overlap region is where it happens.
- **Vertical drag just changes `LayerIndex`** — overlaps form/dissolve from geometry, no attachment bookkeeping.
- **Auto-collect, don't auto-rewire.** The compositor collects clips + overlaps (like `_ProcessVideoClips` / `AudioClipPlayer`); never wire transition ops into the graph on every edit.
- **Transition type = a signature-detected op** ("2 textures + 1 float → 1 texture"), read from `Symbol.InputDefinitions`/`OutputDefinitions` via the `SymbolFilter` type-match; a `Transition` tag on `SymbolUi.SymbolTags` gated on that signature (multi-texture inputs collapse to one `MultiInputSlot<Texture2D>` with `IsMultiInput`).
- **Big unification:** a transition *is* a combinator node in a media-reference graph — the **same pattern as audio mixing**. Two `VideoReference` inputs + a progress float → one node, realized by the compositor. So this generalizes the audio reference-graph to video (see `Plan_AudioProcessingGraph.md` Phase G / open-question on video depth).
- **Hard part:** surfacing the chosen transition op's parameters in the timeline UI (the compositor hosting a transition sub-instance). Ship a few built-in data-transitions (dissolve, dip-to-color, wipe) first, with reference-by-Guid to custom transition ops.

### 4. Within-clip non-destructive audio editing — *needs its own plan*
The Audacity workflow: remove "ahems"/doubles, cut/copy/paste, normalize — **without fragmenting one recording into hundreds of clips.**
- **Non-destructive segment list.** One recording = one clip carrying an internal ordered list of kept source sub-ranges, rendered as a single **healed** waveform. Removing a range = drop a segment + a short edge crossfade; undo = restore the segment. The source WAV is never rewritten during editing.
- Satisfies "it's no longer relevant that anything was ever there" (the removed range simply isn't in the list; the waveform reads continuous). An explicit **bounce/consolidate** bakes a clip to a new WAV when wanted; never per-edit.
- **Two granularities, one clip type:** clips = coarse placement of distinct sources/takes; segments = fine cleanup within one recording.
- **Realization cost:** seeking BASS across skip points reuses the existing resync path (cheap); *click-free* joins need a short overlap/ramp (the real work — a single decode stream can't crossfade with itself). Start with a very short linear dip at seams, refine to equal-power.
- **Cut clicks:** any cut/join needs a few-ms edge crossfade (default auto-fade ~5–10ms).
- **Undo/redo is trivial** — snapshots of a small segment list, not audio buffers. This dissolved the original "extensive memory-block management" worry.
- Orthogonal to the routing graph — it's the clip's *payload*. The amplitude-**waveform** renderer this needs is already folded into `Plan_AudioProcessingGraph.md` (Display/Style); TiXL only has an FFT spectrogram today ([`AudioImageGenerator`](../../Editor/Gui/Audio/AudioImageGenerator.cs)).

### 5. Audio annotation pipeline (markers / Whisper / clap-detection / enhance) — *needs its own plan*
- **Typed timeline markers (net-new — no marker system exists today; the playhead is the only "marker", and `Bookmark` is spatial graph pan/zoom).** Model on `TimeClip`; a **string `Kind` discriminator** (`word` / `filler` / `silence` / `clap` / `takeBoundary` / `chapter`) + `Text` + `Confidence` + an open metadata bag + version field (per the interface-stability audit). Persist on `SymbolUi.TimelineState`; make snappable via `IValueSnapAttractor` (model on `CurrentTimeMarker`); navigate by cloning the jump-to-keyframe actions in `TimeControls.cs`.
- **Sources:** clap-detection (double-clap take markers) via the existing onset detection ([`BeatSynchronizer`](../../Core/Audio/BeatSynchronizer.cs) / `AudioAnalysis`) — cheap, no ML, high value for the VO workflow; **Whisper** → word/filler markers, in its **own package or an editor-side async service** (hot-reload isolation — never in `Core`); manual markers.
- **Actions:** selection-by-kind ("select all ahems" = kind==filler); remove-doubles (the take between two `clap`/`takeBoundary` markers); **bounce-selection-through-external-effect** — one reusable flow ("process selection → new source file → swap the clip's source") serving Adobe Podcast enhance, local normalize, noise removal. External/network services (Adobe) = an explicit publish action, own package.
- The recording flow already spawns parallel audio + `[LoadDataClip]` data clips — the substrate for marker/annotation data.

### 6. NLE timeline editing UX — *needs its own plan or section*
- **Already exists — don't rebuild:** slip (`Ctrl+Alt`), split / cut-at-time, snapping, roll, stretch, start/end trim handles — all in [`TimeClipInteractions`](../../Editor/Gui/Windows/TimeLine/TimeClips/TimeClipInteractions.cs). Modifier scheme: `Alt` = source-link, `Ctrl+Alt` = slip, `Ctrl` = lock-time (layer-only).
- **Cheap once clips are data-model-driven:** select-all-later (filter `TimeRange.Start ≥ t`), ripple insert / remove-gap (shift later clips), linking/anchoring (a shared group `Guid`; the recording flow already makes parallel audio+data clips that *should* be linked).
- **Caution:** FCP's **magnetic timeline** (connected clips, storylines) is a deep rabbit hole and fights the flat `LayerIndex` model — do ripple + snapping first; treat magnetic as a much-later, carefully-scoped item, not an early win.
- **Clip model reality:** a clip *is* an operator instance ([`TimeClip`](../../Core/Animation/TimeClip.cs): `TimeRange` in bars, `SourceRange`, `LayerIndex` = track). Discovered by walking the graph (`Structure.GetAllTimeClips`, flagged slow). Structural edits (create/delete/split) → graph commands; timing edits (move/trim/stretch) → `MoveTimeClipsCommand`.

## Sequencing / relationships

- **Audio processing graph** is the active thread (spike in progress). It proves the reference-graph + BASS-reconciliation pattern.
- **Video transitions** generalize that same combinator pattern to video — best done *after* the audio spike validates it.
- **Within-clip audio editing** is independent of the routing graph (clip payload); the amplitude waveform it needs is already in the audio plan.
- **Annotation** needs the net-new typed markers; clap-detection is a cheap early win.
- **NLE UX** is mostly done; ripple + linking are cheap follow-ups; magnetic is deferred.

## Status

- Detailed: `Plan_AudioProcessingGraph.md`, `Plan_ReferenceLines.md`.
- Captured here, awaiting their own plans: video transitions (§3), within-clip audio editing (§4), annotation pipeline (§5), NLE editing UX (§6).
- Superseded: `Plan_TimelineAudioClips.md` (by §1).
