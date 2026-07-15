# Audio processing graph — clips as reference-emitting operators

Supersedes the "audio clips as data" direction of [`Plan_TimelineAudioClips.md`](Plan_TimelineAudioClips.md). That plan's Phases A–C (the `TimelineAudioClip` payload shape, native-rate playback, N-clip engine) are **kept and reused**; its Phases D–F (rebuild a parallel non-op editing/render path, delete the `[AudioClip]` op) are **cancelled** — this plan goes the other way and makes the op canonical.

## Goal

One audio model: **every timeline audio clip is a real operator.** No data-only clip type, no permanent second system. Audio becomes consistent with video (`[VideoClip]`) and data (`[LoadDataClip]`/`[DataClipPlayer]`), so a new user never asks "where are the AudioClip ops?" and gets "it's complicated" as the answer.

The mechanism generalises TiXL's existing **field / shader graph** (`ShaderGraphNode`): clip/mix/group operators emit a lightweight *reference node* on the wire that declares structure and animatable parameters; a downstream **root** traverses that graph and *realises* it against the BASS mixer. The reference/dataflow/direction distinction is invisible to users — they just wire ops together.

Three usage tiers, all on one substrate:

1. **Fast & dirty** — clips auto-play, no wiring. Traditional main soundtrack. (implicit collection)
2. **Working** — a clip with AutoPlay off and nothing collecting it is silently placed. (opt-in)
3. **Full control** — route/group/mix/duck through operators, keyframe any parameter. (explicit reference graph)

## Implementation status (resume here)

Both architectural risks are **validated by working spike code** (all `_`-prefixed, throwaway, in `Operators/Lib/io/audio/` unless noted):

- **R2 — live BASS submix routing:** ✅ proven. `_AudioBusSpike` (`.cs`/`.t3`/`.t3ui`) — dynamically creates decode submixes under the operator mixer, routes a looping file through one, per-bus gain via `ChannelSetAttribute`, and glitch-free live reroute (`MixerRemoveChannel`→`MixerAddChannel` on a playing source).
- **R1 — off-update-path recursive collection:** ✅ proven. `AudioGraphNode` (`Core/DataTypes/AudioGraphNode.cs`, registered in `Core` `SymbolPackage.RegisterTypes`) + `_AudioSourceSpike` / `_AudioGroupSpike` / `_AudioRootSpike`. The root logs the correct recursive collected source set, updating live as the graph is rewired.

**Next — 2b (the join):** give the leaf `AudioGraphNode` a source channel; have the root create one BASS bus per collected source (reusing `_AudioBusSpike`'s routing) and set each bus's gain from the node — so source→group→root actually *plays*, routed. Fold in hardening in the same rebuild: register `AudioGraphNode` in `Symbol.Child._bypassableTypes` and `TypeUiRegistry` (color), matching `ShaderGraphNode` (a transient NRE appeared mid-wiring while these were absent).

**Throwaway, delete once real ops land:** the five `_`-prefixed spike ops above.

## Architectural decisions (locked in)

- **All clips are ops.** The `[AudioClip]` operator is canonical (reverses `Plan_TimelineAudioClips.md`'s "delete the op" step). The legacy `CompositionSettings.Playback.AudioClips` settings-list drops to **migration-source-only** and is deleted once projects are migrated.

- **No shadow / hidden ops.** (Considered and rejected — Tooll2 had hidden instances and they brought debugging, selection, and consistency drawbacks.) Migration creates an ordinary **visible** `[AudioClip]` op; a typical project has a single main soundtrack, so that's one node, not clutter. Where a project genuinely has many audio ops, density is managed by the **existing, user-controlled collapsed sections** — deliberate and reversible — never by automatic hiding.

- **New connection type `AudioReference` is a *structural node*, not a value.** Modelled on `ShaderGraphNode` (`Core/DataTypes/ShaderGraphNode.cs`), not on `RenderTargetReference`. It flows on `Slot<AudioReference>` and carries: either a reference to a **source** (anything exposing a routable BASS channel — a clip stream, operator/proc stream, or an op-internal submix) *or* references to upstream input nodes, plus a **routing kind** (`Mixable` / `Direct`), animatable params, and a `StructureHash` + `ChangedFlags`. **No PCM ever flows through graph evaluation.** BASS is the executor ("the GPU of audio"); the wire carries structure only. A connection type in TiXL is just a C# value type registered for a wire colour via `TypeUiRegistry.SetProperties(...)` (`Editor/UiModel/UiRegistration.cs`).

- **Collection is root-driven and off the per-frame `Update()` path.** A root (`GenerateAudioProcessingBlock`, hosted by `[AudioClipPlayer]` / the audio output) initiates a bespoke traversal — recursive node `Update()` then a collect pass — exactly like `GenerateShaderGraphCode`. The reference output must be collectable **independent of the playhead** (unlike the current `TimeClipSlot<Command>` output): audibility is computed by the root from each clip's `TimeRange`, not from whether the op's `Update` ran. **The root is responsible for updating the animatable inputs of every node it collects** — mirror `ShaderGraphNode`'s `foreach (paramInput) paramInput.Update(context)`; skip this and automation silently freezes. **Validated (2a spike — `_AudioSourceSpike`/`_AudioGroupSpike`/`_AudioRootSpike`):** a root collecting through group combinators logs the correct recursive source set (`A, B, A` for a source reached via two paths), updating live as the graph is rewired — R1 confirmed, off the per-frame path. Hardening TODO before graduation: register `AudioGraphNode` in `Symbol.Child._bypassableTypes` and `TypeUiRegistry` (color), matching `ShaderGraphNode` — a transient NRE appeared mid-wiring while these were absent.

- **Two collection paths, both with existing infra:**
  - *Implicit (tiers 1–2):* `AudioClipCollector.RegisterAutoPlayClips` already scans `composition.Children` for `IAudioClipProvider` and runs in the Player/export loop too. Works regardless of graph wiring (it scans the instance graph, not the UI). **No connections required.**
  - *Explicit (tier 3):* the `AudioReference` graph, collected by the root.
  - *Precedence:* an explicit root owns any clip it reaches; the implicit collector claims only clips no explicit root took. A clip reached by two roots is an intentional multi-send — the root realises it via split streams internally (§Realisation), so it's user-invisible.

- **`[AudioClip]` display is an explicit setting, not magic.** Two per-clip params replace the overloaded `IsMainSoundtrack` background behaviour:
  - **Display:** `Clip` (a normal timeline block) or `Background image` (full-timeline-width behind the tracks — what the main soundtrack renders as today).
  - **Style:** `Waveform` (amplitude — the renderer TiXL lacks today; only an FFT spectrogram exists), `Volume level` (filled envelope, Audacity / FCP-style), `Precise` (full-resolution waveform on zoom — long-term).
  This extracts the *background-render* concern out of `IsMainSoundtrack` (a documented tech-debt bundle of background-render + FFT-routing + export). Display only solves the render concern; FFT-source and export-inclusion still need their own explicit homes (see open questions).

- **Implicit-relationship lines + "realize" — a general graph feature, not audio-specific.** Auto-collected clip→root relationships render as **faint dashed reference lines**; one action materialises a line into an explicit wire (tier 1 → tier 3), with an inverse that drops the wire back to implicit. The handoff must be glitch-free (no double-play or dropped frame). The same affordance is wanted for **Set/Get Variable** ops and the requested VVVV/Houdini-style **Send/Receive** ops (named wireless links). Build the line rendering + "realize" as a shared graph feature; audio is its first consumer. Note the two underlying flavours it must cover: a *latent real connection* (audio auto-collect — realises into a typed wire) vs a *name binding* (variables / send-receive — may stay wireless, but could also realise into a direct wire to its source).

- **Ducking / sidechain = a metered float, not a structural edge.** A Duck op reads the *last frame's* level of a bus (the engine already meters via `Bass.ChannelGetLevel`) and drives a gain — the ~1-frame lag is inherent to metering and inaudible for ducking. This keeps the reference graph a pure tree, and even makes mutual sidechain degrade to a stable discrete feedback loop instead of a hard cycle. Only *sample-accurate* keying (a look-ahead limiter keyed off another bus) stays structural, and is deferred. Cleanest form: an `AudioLevel(ref) → float` tap op + plain float math into a Volume, so no special "signal vs key" reference variant is needed.

- **Realization is a *diff against live state*, not a rebuild.** This is where the audio version is genuinely harder than the shader graph (which is stateless pure-compute). `ChangedFlags` drive an incremental reconciliation of the BASS mixer: `Structural` → add/remove/reroute streams, insert/remove FX (fading FX tails so a removed reverb doesn't truncate); `Parameters` → set scalar volumes/pans every frame unconditionally (cheap — no compile to protect, so no per-param caching needed). This reconciliation is a *superset* of what `AudioEngine` already does (heartbeat, stale-eviction, resync-threshold), so it extends existing code rather than starting fresh.

- **Back-compat:** no forced load-time mutation. Migrate on save (the file already follows "clean in memory on load, persist on next save" — see `CompositionSettings.GetClips` dead-clip pruning). `CompositionSettings.TryGetMainSoundtrack` generalises to a **union** of the settings-list and any op flagged main, so the background-waveform / FFT / export lookups keep working for old and new projects. Existing playback and audio images are untouched.

## Data model

`AudioGraphNode` (new, `Core/Audio/` — the value carried by `AudioReference`), shaped after `ShaderGraphNode`:

```csharp
sealed class AudioGraphNode
{
    // Leaf: any source exposing a routable BASS channel — a clip stream, an
    // operator/proc stream, or an op-internal submix (a future polyphonic sampler
    // sums its own voices and exposes one channel here). Null for pure combinators.
    IAudioSourceChannel? Source;

    // Direct sources (spatial HW-3D) route to the output/3D engine, not a submix,
    // and can't take a bus FX insert. Mixable is the default.
    RoutingKind Routing;                  // Mixable | Direct

    // Combinator: upstream nodes folded into this one (from the op's input slots).
    List<AudioGraphNode> InputNodes;      // rebuilt each traversal; drives StructureHash

    // Animatable params collected by the root each frame (volume, pan, sends, FX params).
    // Analogous to ShaderGraphNode's [GraphParam] inputs.
    // ...

    int StructureHash;                    // detects rewiring without DirtyFlagTrigger
    ChangedFlags CollectedChanges;        // Structural | Parameters (no "Code" — no compile)
}
```

Reused unchanged from `Plan_TimelineAudioClips.md`: `TimelineAudioClip` (the clip payload the handle wraps), `AudioClipResourceHandle`, native-rate playback, `TimeRange`/`SourceOffsetSecs`/`SourceDurationSecs`. `Display`/`Style` are ordinary inputs on `[AudioClip]`.

Placement math is already converging on the shared `TimeRangeMapping` struct (`Core/Animation/TimeRangeMapping.cs`), whose doc already names audio/video/data/image-sequence as intended consumers.

## Operators

- **Package placement (found during the spike):** audio-graph ops call `BassMix` directly, so they must live in a BASS-referencing package (`Lib`, like `AudioToneGenerator`). User/Playground projects set `DisableTransitiveProjectReferences` and reference only Core/Logging/SharpDX — they *cannot* call BASS. (Alternative: Core exposes sub-bus routing helpers so any package can drive audio.)
- **`AudioReference`/`AudioGraphNode` must be a *Core* type (found during 2a):** connection/input types are registered centrally in `Core` `SymbolPackage.RegisterTypes` (`InputValueCreators`, mirroring `ShaderGraphNode`). A Lib-defined type can't be registered there, so a `MultiInputSlot<AudioGraphNode>` input fails to build its input definition ("can't create Input Definition"). Outputs are unaffected — only inputs need the registration. So the wire type lives in `Core/DataTypes` (beside `ShaderGraphNode`); the ops live in `Lib`.
- **Sources (leaves):** `[AudioClip]` (timeline clip; `Display` + `Style` params). The trigger-driven operator-audio ops — `[AudioPlayer]`, `[AudioToneGenerator]` (a procedural synth), `[PlayAudioClip]` — become non-timeline source nodes that *also* emit `AudioReference`, so a triggered synth voice and a timeline clip route / mix / FX through identical machinery. **Monophonic today** (1 op = 1 voice = 1 reference); trigger / ADSR / pitch stay on the op, below the reference (the reference is its output bus). `[SpatialAudioPlayer]` is a routing exception — a `Direct`-kind source (§Data model).
- **Combinators:** `[AudioGroup]` / `[AudioMix]` (fold `MultiInputSlot<AudioReference>`, adjust gain/pan), `[AudioGain]`, `[AudioReverb]` / `[AudioEffect]` (declare an FX insert on the collected bus).
- **Root:** `[AudioClipPlayer]` hosting `GenerateAudioProcessingBlock` (≈ `GenerateShaderGraphCode` for audio). Traverses, collects, reconciles BASS.
- **Metered control:** `[AudioLevel]` (bus → last-frame float), `[Duck]` (level → gain envelope). Ordinary float dataflow, on the normal `Update()` path.

## Realisation → BASS (the load-bearing new work)

- **Validated (spike `_AudioBusSpike`, Lib/io/audio):** dynamically creating decode submixes nested under the operator mixer, routing a source through one, per-bus gain via `ChannelSetAttribute`, and **glitch-free live reroute** (`MixerRemoveChannel`→`MixerAddChannel` on a *playing* source) all confirmed working by ear. The core reconciliation mechanism — the one part with no codebase precedent — is proven. Remaining work is doing it *at scale*: many sources/buses, add/remove per frame, FX inserts with tail fades. (The reroute was click-free in the simple case; still ramp gains on structural changes as a safety margin under heavier rerouting.)
- **What the reconciler fundamentally does:** every source already produces a BASS channel (clip stream / operator stream / proc stream / op-internal submix); today each op *hard-codes* which mixer its channel joins (`OperatorMixer` / `SoundtrackMixer` / direct-3D). Realisation makes that a routing decision the root owns — reconciliation is the set of `MixerAddChannel` + FX-insert + gain calls that plug each collected source's channel into the resolved (sub)mixer. Sources keep producing channels exactly as today; the root owns the mixer topology.
- **Direct sources** (`RoutingKind.Direct`, e.g. spatial HW-3D) route straight to the output / 3D engine, bypassing submix insertion. A group/FX node that collects one respects its gain but can't bus-insert it (a validation hint, not a silent stereo-collapse).
- `[AudioGroup]` → a `BassMix` submix; `[AudioReverb]` on it → `ChannelSetFX` on that submix; the root plugs the submix into master.
- Params (volume/pan/FX mix) → `ChannelSetAttribute`, set every frame unconditionally.
- Topology mutations (create/destroy/reroute streams, insert/remove FX) → gated on `StructureHash` change only.
- **Multi-send** (decided — allowed, handled internally): a source feeding two groups → BASS split streams (`BASS_Split_StreamCreate`), since a decode source sits in only one mixer. The root's reconciler owns the splitters entirely — the user just wires a source into two buses and it sums; the split-stream mechanism is never exposed.
- FX removal fades the tail rather than truncating; stream add/remove reuses the existing stale/resync path.

## Migration (settings-list → visible ops)

- For each `TimelineAudioClip` in `Playback.AudioClips`, create an ordinary **visible** `[AudioClip]` op (AutoPlay on; path / `TimeRange` / source-offsets / volume / mute copied; former main soundtrack gets `Display = Background image`, `Style = Waveform`), then clear the migrated entry. Reuse the op-insertion `RecordingSession` already performs when it spawns an `[AudioClip]` during recording. Run on **save**, not load (matches `CompositionSettings.GetClips`' clean-on-load / persist-on-save idiom).
- Placement: drop the migrated op at a sensible spot (near the composition output / off to one side) so it doesn't overlap existing nodes.
- Generalise `TryGetMainSoundtrack` to union settings-list + op-flagged clips during the transition, so background waveform / FFT / export keep working before and after migration.

## Phases

### Phase A — `AudioReference` substrate + implicit collection (tiers 1–2)
Introduce the `AudioGraphNode`/`AudioReference` type + `TypeUiRegistry` colour. `[AudioClip]` emits an `AudioReference` (playhead-independent) and keeps its `IAudioClipProvider` handle. Implicit child-scan collection (`AudioClipCollector`) realises leaf clips at unity into the engine — coexisting with the existing settings-list playback so **nothing breaks**. Depth-1 graphs only (no combinators yet).
*Outcome:* dropping/placing an `[AudioClip]` plays via auto-collect; existing projects unaffected.

### Phase B — Settings-list migration + clip Display/Style
Migrate `Playback.AudioClips` → visible `[AudioClip]` ops on save; `TryGetMainSoundtrack` union. Add the `Display` (Clip / Background image) and `Style` (Waveform / Volume level) params, including the amplitude-**waveform** renderer TiXL currently lacks (only an FFT spectrogram exists today). `Precise` full-res-on-zoom deferred.
*Outcome:* old projects load unchanged; on save the soundtrack becomes a visible op rendered as a background waveform; audio images intact.

### Phase C — Root + combinators + BASS reconciliation (tier 3)
`GenerateAudioProcessingBlock` root; `[AudioGroup]`/`[AudioMix]` combinators; the `StructureHash`-driven diff against the live mixer. Explicit-root ownership precedence vs the implicit collector.
*Outcome:* wire clips → groups → output; bundle two clips and adjust their volumes; reroute without glitches.

### Phase D — Implicit-relationship lines + "realize"
Delivered by the separate [`Plan_ReferenceLines.md`](Plan_ReferenceLines.md) (the general graph affordance). Audio is its **first-class realize case**: the clip→root reference has a real slot pair, so a dashed line materialises into an actual `AudioReference` wire via `AddConnectionCommand` with a glitch-free ownership handoff. This plan just registers the audio matcher and supplies the realize command.
*Outcome:* a fast-and-dirty project's auto-routing is visible and becomes explicit in one click.

### Phase E — Metered control ops
`[AudioLevel]` + `[Duck]`; per-bus level exposure from the engine.
*Outcome:* duck music under a VO clip; sidechain pump.

### Phase F — Effects + multi-send
`[AudioReverb]`/`[AudioEffect]` FX inserts with tail-fade on removal; split-stream multi-send.
*Outcome:* reverb on a group; one clip sent to two buses.

### Phase G — Generalise + retire legacy
Video transitions as `VideoReference` combinators (the "2 tex + float → 1 tex" op is an SDF-style combinator); align data via the `[DataClipPlayer]` rename + AutoCollect (#1078). Delete the settings-list playback path and archive `Plan_TimelineAudioClips.md`.
*Outcome:* one reference-graph pattern across audio/video/data; legacy audio path gone.

## Open questions / deferred

1. **Single-root vs multi-send** — *Resolved:* multi-send is allowed; the AudioProcessing root owns the realisation (split streams) internally, so it's invisible to the user and can't become an accidental footgun. See §Realisation.
2. **FX backend quality.** BASS DX8/BASS_FX built-ins vs hosting something better. The architecture is agnostic (FX is a bus property); quality is a separate call.
3. **`IsMainSoundtrack` full unbundle.** `Display` takes the background-render concern; FFT-source routing and export-inclusion still need explicit homes (a connection/flag each) rather than one magic bool.
4. **Default `Display`/`Style`** for a newly-created `[AudioClip]` (Clip + Waveform likely; a dropped soundtrack might default to Background image).
5. **Reference-line feature** — *Resolved:* split into [`Plan_ReferenceLines.md`](Plan_ReferenceLines.md); this plan consumes it (audio is its first-class realize case).
6. **Video re-architecture depth.** Keep frames flowing on the wire and wrap structure in a `VideoReference` node, vs a fuller reference-on-wire rework. Don't over-unify video.
7. **Sample-accurate sidechain** (look-ahead limiter keyed off another bus) — stays structural; deferred.
8. **Sends vs export mixdown** — how multi-send interacts with the sample-accurate export path (`AudioRendering`).
9. **Spatial audio routing** — *Resolved (contract):* the reference carries a **routing kind** and spatial is `Direct` — routed to the HW-3D / output path, not a submix, so it isn't group/FX-able (confirmed: no `MixerAddChannel`, excluded from FFT/metering, separate export path). The discriminator is baked into the Phase-A contract (cheap now, a wire-shape migration later). *Deferred (feature):* an optional *software spatializer* node that pans/HRTFs into the mixer to make spatial routable at the cost of HW 3D.
10. **Event-driven sampler + polyphony** — *Resolved (safe to defer, zero contract risk):* since the reference points at "a routable channel" (§Data model), a future polyphonic op just sums its voices into its *own* internal submix and exposes that as its channel — the root routes it identically to a mono source. The event input is an ordinary `InputSlot<DataClip>`, orthogonal to the output contract. So nothing about the sampler needs baking in now; it's purely future feature work (a source op that consumes a `DataClip` event stream and spawns a voice per event — the socket the two plans create together).

## Back-compat

Covered above: no load-time mutation, migrate on save, `TryGetMainSoundtrack` union, `[AudioClip]` op kept canonical (transitional-phase projects that already contain it stay valid). Existing playback and spectrogram/waveform images work untouched until (and after) migration.

## Manual test sets

- `audio-clip-op-playback.md` — place `[AudioClip]`, auto-plays; scrub re-syncs; export includes it.
- `audio-legacy-migration.md` — open an old settings-list soundtrack project: plays + images unchanged; save → becomes a visible op; reopen still works.
- `audio-display-style.md` — toggle Clip / Background image and Waveform / Volume level; renders correctly.
- `audio-routing.md` — group two clips, adjust volumes, add reverb, reroute live without glitches.
- `audio-realize.md` — reference lines visible; realise promotes to explicit wires; inverse returns to implicit.
- `audio-ducking.md` — duck music under a VO clip.
```
