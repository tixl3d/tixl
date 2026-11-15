# Plan: Typed System State Instead of Hidden Context Variables

Status: **Phase 1 implemented 2026-11-15, untested by a human.** `Core/Rendering/RenderPassState.cs` (only
`VelocityRequested`), `EvaluationContext.RenderPass` (a field, cleared in `Reset()`), `[RenderTarget]` sets and restores
it around its scene, `[GetRenderPassState]` (`flow/context`, `Select` dropdown, int output, `DirtyFlagTrigger.Always`),
`[DrawMeshAtPoints]` picks current vs. previous points with `[PickBuffer]`.
Bridge check 2026-11-15: `KeepPreviousPointBuffer` runs once per frame with `WithVelocityBuffer` on and not at all with
it off; velocity on moving instances unchanged; no shader or log errors.

**Phase 2 implemented 2026-11-15, untested by a human:** `RenderPassState.RepeatPass`, `CubeFace` and
`IsRepeatOfFrame` (either > 0). All ten C# readers use `IsRepeatOfFrame`; `EvaluationHistory` keys by
`RepeatPass * 6 + CubeFace`, so cube maps inside motion blur no longer collide. Compatibility instead of new graph ops
for now: `[Loop]` sets `RepeatPass` when its index variable is named `RenderPassState.LegacyRepeatPassVariable`
(`"__MotionBlurPass"`) and restores it after the loop; `[RenderToCubeMap]` sets `CubeFace` and still writes the legacy
`"CubeFace"` variable, but no longer adds the face to the pass number. Side fix: `[Loop]` never restored its variable,
so stateful ops evaluated after `[RenderWithMotionBlur]` in the same frame saw the last pass and stopped advancing; the
typed state is restored. `[RepeatFrame]` (replacing the named loop in the two motion blur graphs) is still open.

Repeat-pass audit (2026-11-15, user noticed `[DampVec3]` getting snappier with more motion blur passes): stateful ops
that never checked the repeat pass advanced once per pass. Now skipping repeats: `[DampVec3]`, `[DampVec2]`,
`[DampAngle]`, `[DampFloatList]`, `[DampPeakDecay]`, `[OrbitCamera]` (damping). `[ActionCamera]` was already safe (it
advances on `Playback.FrameCount`). Still to decide, they count or detect changes and would see one change per
subframe: `[CountInt]`, `[HasValueChanged]`, `[HasIntChanged]`, `[HasVec2Changed]`, `[HasVec3Changed]`,
`[DeltaSinceLastFrame]`, `[WasTrigger]`, `[PeakLevel]`, `[DetectPulse]`, `[DuckAudioLevel]`, `_DetectBeatOffset`.

**Phase 2b done 2026-11-15 (user-tested: "works really well"): `[RepeatFrame]` with subframe steps.** Skipping repeats
made damped values constant across motion blur subframes (no blur). Instead of interpolating, stateful ops now advance
in smaller steps:
- `RenderPassState.IsSubframe`, `SubframeStepSec`, `GetStepDurationSec(frameDurationSec)` (subframe share; 0 for further
  cube faces and other repeats; otherwise the whole frame). `DampFunctions` overloads take the step duration.
- `[RepeatFrame]` (`render/utils`): `Passes`, `Shutter` (window as part of the last frame, clamped to 0–1 because state
  from before the previous frame is gone). Subframes run in time order and end at the frame time; the first step covers
  the time since the previous frame, the rest split the window, so the steps add up to one frame.
- `[RenderWithMotionBlur]` and `[RepeatWithMotionBlur]` use it instead of `[Loop]` + `[SetTime]` + `__MotionBlurPass`
  (also fixes `[RepeatWithMotionBlur]` setting the pass via `[SetIntVar]`, which typed readers didn't see). `Strength`
  is now the shutter; values above 1 act as 1.
- Damping (`[Damp]`, `[DampVec2]`, `[DampVec3]`, `[DampAngle]`, `[DampFloatList]`, `[OrbitCamera]`) steps by
  `GetStepDurationSec`. `[Spring]`/`[SpringVec2]`/`[SpringVec3]` became time-based (60 Hz steps split into partial steps
  of at most one frame; identical at 60 fps). `[Ease]`/`[EaseVec2]`/`[EaseVec3]` start a new animation only on the
  frame's first pass but evaluate the curve at each subframe's time. `[DampPeakDecay]` still advances once per frame
  (it uses global time).
- Result: damped motion is blurred and its speed doesn't depend on the pass count.

**Phase 3 implemented 2026-11-15, untested by a human.** No code or graph uses `__IsShadowPassId`,
`_TXAA_JitterOffset` or `OverrideMotionBlurSamples` anymore.
- `RenderPassState`: `IsShadowPass`, `JitterOffset` (pixels), `SubframeCount` (set by `[RepeatFrame]`, for the
  accumulation weight inside the subframes; a `[RepeatFrame]` output would make a graph cycle), `SubframeCountOverride`
  (set by the editor's output window during export, > 0 only, like the export setting).
- New `[SetRenderPassState]` (`flow/context`): `Select` (IsShadowPass, JitterOffset), `Value`, `Offset`; `SubGraph`
  connected = scoped, unconnected = rest of the frame (like `[Set*Var]`). `[GetRenderPassState]` gained RepeatPass,
  CubeFace, IsShadowPass, SubframeCount (appended).
- Core `CameraJitter` reads `RenderPass.JitterOffset`; `[RepeatFrame]` applies the override and sets `SubframeCount`.
- Graphs changed in place (same child ids and positions): `[SetShadow]` (2×), `[TXAA]` → `[SetRenderPassState]`;
  `[DrawMeshWithShadow]`, `[DrawMeshChunksAtPoints]`, both motion blur graphs → `[GetRenderPassState]`; `Passes` goes
  straight into `[RepeatFrame]`.
- Not moved, by user decision: `SpeedFactorA/B` (Core `AnimMath`), `__TriggerA/B` (`[WasTrigger]`, VJ examples),
  `_normalizedTime` (`[TimeClip]`), `_PlayModeProgress` (editor skill levels), `iterator` (obsolete ops) stay user
  variables, documented in `.help/docs/advanced/ContextVariables.md` (with the legacy `__MotionBlurPass` / `CubeFace`).

**Phase 4 done 2026-11-15:** help page `.help/docs/advanced/ContextVariables.md` (variables, scoping, loops, render pass
state, conventional names, C# usage) and the rule "Engine state is typed, not a named context variable" in
`AGENT_INSTRUCTIONS.md`. The legacy readers stay: `[Loop]` still maps an index variable named `__MotionBlurPass` to the
repeat pass, `[RenderToCubeMap]` still writes `CubeFace`.

## Goal

Ops talk to each other through context variables with magic names (`"__MotionBlurPass"`, `"__IsShadowPassId"`,
`"_TXAA_JitterOffset"`, ...). The names are string literals repeated across C# and `.t3` files, undocumented, typo-prone,
invisible in the UI, and every access hashes a string and looks up a dictionary.

Split the two things these variables are used for:

- **System state**: values the engine's own ops set and read (repeat pass, cube face, shadow pass, TAA jitter, velocity
  request, ...). These become **typed, documented members** of `EvaluationContext`, with scoped set/restore and one
  graph op to read them.
- **User variables**: `[Set*Var]` / `[Get*Var]` by name. These stay as they are. The parked
  `Plan_ContextVariableInvalidation.md` covers their performance (typed slots instead of dictionaries).

New system state never gets a string name again. The first case is the velocity request for
`Plan_ImplicitRenderDefaults.md`.

## Inventory (2026-11-15)

| Name | Type | Written by | Read by | Meaning |
|---|---|---|---|---|
| `__MotionBlurPass` | int | `[RenderWithMotionBlur]`, `[RepeatWithMotionBlur]` (as `[Loop]` index variable), `[RenderToCubeMap]` (adds the face index) | `HasTimeChanged`, `Damp`, `Spring`, `Ease`, `EaseVec2/3`, `SpringVec2/3`, `AudioReaction`, `EvaluationHistory` (Lib Utils) | > 0: this evaluation repeats the current frame, don't advance state; also keys per-pass history |
| `CubeFace` | int | `[RenderToCubeMap]` | user graphs only | face being rendered |
| `__IsShadowPassId` | int | `[SetShadow]` (graph) | `[DrawMeshWithShadow]`, `[DrawMeshChunksAtPoints]` (graphs) | the scene is drawn into a shadow map |
| `_TXAA_JitterOffset` | Vector2 | `[TXAA]` (graph, `[SetVec2Var]`) | `CameraJitter` (Core) | sub-pixel jitter cameras apply |
| `OverrideMotionBlurSamples` | int | Editor `OutputWindow` | `[RenderWithMotionBlur]`, `[RepeatWithMotionBlur]` (graphs) | sample count override from the output window |
| `SpeedFactorA` / `SpeedFactorB` | float | `[SetSpeedFactors]`, VJ example graphs | `AnimMath` (Core), obsolete `Counter`, `_AnimValueOld` | global animation speed factors |
| `_normalizedTime` | float | `[TimeClip]` | user graphs only | progress through the time clip |
| `_PlayModeProgress` | float | Editor `SkillTraining` | skill level graphs | play-mode progress |
| `__TriggerA` / `__TriggerB` | float | VJ example graphs (`SetVJVariables`) | `WasTrigger` | trigger inputs of the VJ setup |
| `iterator` | int | obsolete `Iterator`, `IterateList` | user graphs | obsolete |

Not context variables, only matched the grep: `__padding` fields in shader-graph cbuffers, Spout interop symbols.

Observed problems:

- **One name, two meanings.** `__MotionBlurPass` means both "repeat of this frame" (checked as `> 0`) and "which pass"
  (history keys). `[RenderToCubeMap]` inside `[RenderWithMotionBlur]` adds face to pass, so pass 1 face 0 equals pass 0
  face 1.
- **Graph writers depend on a string typed into a `[Loop]` field.** Renaming the loop variable silently breaks
  motion blur for every stateful op.
- **Core reads names set by Lib graphs** (`_TXAA_JitterOffset`, `SpeedFactorA/B`): an undocumented contract across the
  package boundary.

## Design

### `RenderPassState` on the context

A small struct on `EvaluationContext`, documented member by member:

```csharp
public struct RenderPassState
{
    /// <summary>Index of this evaluation among repeats of the same frame (motion blur samples, cube faces). 0 = the frame itself.</summary>
    public int RepeatPass;
    /// <summary>Cube face being rendered by [RenderToCubeMap], or -1.</summary>
    public int CubeFace;
    /// <summary>The scene is drawn into a shadow map; draws can skip work the shadow doesn't need.</summary>
    public bool IsShadowPass;
    /// <summary>The render target being drawn has a velocity buffer; draws keep the history it needs.</summary>
    public bool VelocityRequested;
    /// <summary>Sub-pixel projection offset of temporal AA, applied by cameras.</summary>
    public Vector2 JitterOffset;
}
```

- `context.RenderPass` holds it. Writers save the whole struct, change their member and restore it afterwards (a
  struct copy, no allocation). This is the same discipline as camera matrices today.
- `RepeatPass` and `CubeFace` are separate members, which fixes the pass/face collision. History keys combine them.
- Not every inventory entry belongs here. `SpeedFactorA/B` and the trigger values are animation and VJ inputs, not
  render pass state. Candidates: a second struct (`AnimationState`) or staying user variables with documented names.
  Decide per entry in Phase 3.

### One graph op to read it

`[GetRenderPassState]` (Lib, `flow/context`): a `Value` dropdown (`RepeatPass`, `CubeFace`, `IsShadowPass`,
`VelocityRequested`) and a single `int` output; booleans are 0/1. One output instead of one per member: ops with many
outputs get cumbersome (user decision 2026-11-15), and an int connects directly to `[PickBuffer].Index` or a
`[Switch]`. The description lists what each entry means. `JitterOffset` is read only by C# (Core `CameraJitter`) and
is not in the dropdown. It replaces `[GetIntVar "__IsShadowPassId"]` and friends inside Lib graphs.

Writers that are graphs today get a C# op or an option:

- `[RenderWithMotionBlur]` / `[RepeatWithMotionBlur]`: a `[Loop]`-like C# op `[RepeatFrame]` that sets `RepeatPass` per
  iteration, instead of naming the loop variable `__MotionBlurPass`.
- `[SetShadow]`: a `[SetRenderPassState]` command op (scoped), or `IsShadowPass` set in C# where the shadow pass runs.
- `[TXAA]`: sets `JitterOffset` through the same command op.

### Compatibility

User graphs and old projects may read the old names with `[Get*Var]`.

- **Read side:** `[Get*Var]` checks a small static table of legacy names (`"__MotionBlurPass"` → `RepeatPass`,
  `"CubeFace"` → `CubeFace`, ...) once when its name input changes, and then reads the typed member. No per-frame
  string work. The table lives in one file with a comment per entry.
- **Write side:** `[Set*Var]` with a legacy name writes the typed member the same way, so user graphs that drive
  motion blur by name keep working.
- The editor's variable dropdowns list the legacy names with their new meaning.
- Remove the table once no Lib graph and no example uses the names (check `.t3` files with grep).

### Documentation

- xmldoc on each member (above), the `[GetRenderPassState]` description, and a section in `.help/` listing the system
  state for people writing custom ops.
- AGENT_INSTRUCTIONS gets one rule: no new string-named context variables for engine state; add a typed member.

## Phases

1. **Velocity request (with the motion-vector work).** `RenderPassState` with only `VelocityRequested`;
   `[RenderTarget]` sets and restores it; `[GetRenderPassState]` with only that dropdown entry; `[DrawMeshAtPoints]` uses it to
   pick between current points and `KeepPreviousPointBuffer`. No legacy table yet.
2. **Repeat pass and cube face.** Move `__MotionBlurPass` and `CubeFace` to typed members, fix the collision, switch the
   ten C# readers (incl. `EvaluationHistory`), add `[RepeatFrame]`, rewire `[RenderWithMotionBlur]` and
   `[RepeatWithMotionBlur]`. Legacy table for both names. Test: motion blur with `[Damp]` / `[Spring]` and with
   `[DrawMovingPoints]` looks the same as before; cube map inside motion blur.
3. **Shadow pass, jitter, sample override.** `__IsShadowPassId`, `_TXAA_JitterOffset` (Core `CameraJitter` reads the
   member), `OverrideMotionBlurSamples` (editor writes a member instead of a dictionary entry). Decide on
   `SpeedFactorA/B`, triggers, `_normalizedTime`, `_PlayModeProgress`.
4. **Docs and rule.** `.help/` section, AGENT_INSTRUCTIONS rule, remove the legacy table when unused.

## Risks

- **Core change.** `EvaluationContext` is Core API used by the Player. Justified: Core itself reads two of these values
  (`CameraJitter`, `AnimMath`). Keep the struct small and documented.
- **User graphs** reading or writing the legacy names: covered by the table. Unknown external projects may use other
  names this inventory missed; the table only covers what we know.
- **Scoping bugs.** A writer that forgets to restore leaks state into siblings (today's dictionary has the same risk).
  A debug assertion in `[RenderTarget]` (state on exit equals state on entry) would catch it.

## Overlap

- `Plan_ContextVariableInvalidation.md` (parked): user variables and their caching. This plan removes engine state from
  that table, which makes its inspector show only user variables.
- `Plan_ImplicitRenderDefaults.md`: Phase 1 here is the velocity request it needs.
