# DopeSheet Per-Parameter Curve Expand

**Date:** 2026-04-19
**Status:** Planning — not yet implemented

## Goal

Let the user pop open an inline curve editor directly from the dope sheet on a per-parameter basis. When one or more parameters are "expanded", a combined curve-editor pane appears below the dope sheet (splitting the timeline body vertically); when all are collapsed, the pane disappears and the dope sheet reclaims the space.

This is positioned as an incremental step toward possibly retiring the standalone `Modes.CurveEditor` view — but that mode stays in place for now. User decides later.

## Non-goals (v1)

- No per-component *expand* toggle — expand is per-parameter. Component visibility inside the expanded view is a separate, multi-select filter.
- No shared-U-across-vector-components dragging (user flagged as "maybe later").

## Shared state

New owner: `TimeLineCanvas`. Both `DopeSheetArea` and `TimelineCurveEditArea` read/write via canvas reference.

```csharp
// on TimeLineCanvas (or a small TimelineSharedState it owns)
public readonly HashSet<int> ExpandedParameterHashes = new();
public readonly Dictionary<int, int> VisibleComponentMask = new();   // paramHash -> bitmask of visible curves

// Cross-view hover link. Written by whichever view the mouse is over;
// both views read these to emphasize the matched objects.
public int?  HoveredParameterHash;     // null = no parameter hovered
public int   HoveredComponentBit;      // 0 = whole parameter; non-zero = single component
public int?  HoveredKeyframeUniqueId;  // null = no keyframe hovered; VDefinition.UniqueId otherwise

public bool  NormalizeCurveView;                                      // pane-level toggle
public float CurvePaneHeightRatio = 0.5f;                             // 0..1, persisted
```

- `VisibleComponentMask[hash]` defaults to "all bits on" when a parameter first expands. Toggling curve-expand off removes the entry entirely, so re-expanding resets component visibility (explicit user requirement).
- **Hover state is a single source of truth, shared across dope sheet and curve pane.** Each view sets these fields when the mouse is over one of its objects; the other view reads them to render matching emphasis. Cleared at end-of-frame if no view claimed them. This generalizes the earlier "component hover" sketch into a full dope-sheet ↔ curve-pane link.

`_pinnedParameterComponents` on `TimelineCurveEditArea` is removed — the component buttons it currently renders in its own param list get extracted and moved to the dope-sheet row (see Phase 3).

## Phase 1 — Layout rework

**TimeLineCanvas.cs**
- In `Modes.DopeView`: if `ExpandedParameterHashes.Count > 0`, split the timeline body child into two nested children:
  - `##dopeSheetPane` — height = `total * CurvePaneHeightRatio` (top). Own scrollbar.
  - `##curvePane` — remaining height below. Own scrollbar. Hosts `TimelineCurveEditArea.Draw(...)` filtered to expanded params only.
- When `ExpandedParameterHashes.Count == 0`, layout reverts to today's single-child dope sheet.
- `Modes.CurveEditor` is untouched.

**Curve pane chrome — floating overlay (no header strip)**
- Buttons float over the canvas in the top-right corner of `##curvePane`, drawn *after* the curve contents so they sit on top. No dedicated header row — saves vertical space.
- Rendered with a subtle translucent background (e.g. `UiColors.BackgroundFull.Fade(0.6f)`) only when hovered, so they stay unobtrusive over dense curves.
- Icon size: **15 px square × `T3Ui.UiScaleFactor`** (matches existing icon metrics). Button hit-box = icon size + a few px of padding.
- Small padding from the pane edge (e.g. 4 px × `T3Ui.UiScaleFactor`). Right-aligned, laid out right-to-left: [Normalize] [Close].
- Hit-testing: use real ImGui buttons placed via `SetCursorScreenPos` before the rest of the canvas interaction, so they consume input first. The fence-select / canvas-pan / keyframe-add paths must check `!ImGui.IsAnyItemHovered()` (most already do) — audit during impl.
- Buttons:
  - Close (`Icon.ChevronDown` or `Icon.Close`) — clears `ExpandedParameterHashes` and `VisibleComponentMask`, collapses the pane.
  - Normalize toggle (see Phase 5) — icon lit when active.

**Splitter**
- v1 commit: fixed 50/50 (`CurvePaneHeightRatio = 0.5f`). See Phase 4 for draggable.

**TimelineCurveEditArea**
- Accept a filter (list of parameter hashes) so it only renders expanded params when driven from the dope-sheet pane.
- Its built-in parameter/component button list is hidden when invoked in this "inline" mode (a parameter to `Draw`, e.g. `showParameterList: false`).

## Phase 2 — 3-segment parameter row

The existing single `InvisibleButton` at [DopeSheetArea.cs:140](Editor/Gui/Windows/TimeLine/DopeSheetArea.cs:140) becomes three stacked invisible buttons, rendered left-to-right inside the label gutter:

1. **Pin toggle** — current behavior, icon `Icon.Pin` / `Icon.PinOutline`. ~14 px wide.
2. **Curve-expand toggle** — toggles membership in `ExpandedParameterHashes`. Icon placeholder `Icon.InterpolationBrokenTangents` (user-provided). On toggle-off, clears the param's entry in `VisibleComponentMask`.
3. **Name button** — sized to text. Click semantics over the param's keyframes (union of all curve `VDefinitions`):
   - Plain click → `SelectedKeyframes = {paramKeys}` (replace)
   - Shift+click → `SelectedKeyframes ∪= {paramKeys}` (add)
   - Ctrl+click → `SelectedKeyframes \= {paramKeys}` (remove)

Modifier logic mirrors `TimeSelectionArea`'s click-on-bucket path documented in [Plan_TimelineSelectionUI.md](../Plans/Plan_TimelineSelectionUI.md).

`MouseClickChangedSelection = true` is set on any modifier-driven change so downstream view-refit logic fires.

## Phase 3 — Component buttons + cross-view hover link

Helping the user mentally map curves ↔ dope-sheet layers is an essential goal of this feature. Every hover interaction in either view should emphasize the corresponding objects in the other view.

### Component buttons in dope-sheet row

For expanded parameters with more than one curve, render N small toggle buttons after the name, where N = curve count. Labels use `CurveNames` / `ColorCurveNames` (already exist in `DopeSheetArea`).

**Click (visibility)**
- Flip the bit in `VisibleComponentMask[hash]`. If the result is 0, flip it back to all-on (can't hide everything — same rule the existing curve editor would need).

**Visibility render**
- Bits cleared in `VisibleComponentMask[hash]` skip rendering in both the dope sheet's curve overlay and the inline curve pane. Keyframe icons in the dope sheet row still render for hidden components (the dope sheet's row is parameter-level, not component-level).

**Code move**
- Lift the component-button block from [TimelineCurveEditArea.cs:106-146](Editor/Gui/Windows/TimeLine/TimelineCurveEditArea.cs:106) into a small helper (e.g. `DopeSheetArea.DrawComponentToggles(param, hash)`), or a static method on a new `ComponentFilter` struct the canvas exposes. Decide during impl.

### Cross-view hover — sources

Each frame, whichever view detects a mouse-over writes the shared hover state first; fallbacks apply in priority order:

| Source (where mouse is)                                      | `HoveredParameterHash` | `HoveredComponentBit` | `HoveredKeyframeUniqueId` |
| ------------------------------------------------------------ | ---------------------- | --------------------- | ------------------------- |
| Dope-sheet layer background (row)                            | param hash             | 0 (whole)             | null                      |
| Dope-sheet keyframe icon                                     | param hash             | 0                     | vDef.UniqueId             |
| Dope-sheet component toggle button                           | param hash             | `1 << curveIndex`     | null                      |
| Curve line segment in curve pane                             | param hash             | `1 << curveIndex`     | null                      |
| Curve-pane keyframe icon (CurvePoint)                        | param hash             | `1 << curveIndex`     | vDef.UniqueId             |
| Nothing                                                      | null                   | 0                     | null                      |

Curve-line hit-testing is needed for the first time — see [TimelineCurveEditArea.DrawCurveLine](Editor/Gui/Windows/TimeLine/TimelineCurveEditArea.cs:487). Add a lightweight pass: for each rendered polyline, check distance from mouse to nearest segment (tolerance ~4 px × `T3Ui.UiScaleFactor`); first match wins. This runs per-frame, but curves already cap at `MaxPolylinePoints = 2000` and only visible curves need testing. If it ever shows in a profile, gate behind a "mouse in curve pane" check.

Hover state is cleared at the end of the frame if no source claimed it.

### Cross-view hover — render emphasis

| Target                                                 | Behavior                                                                                                  |
| ------------------------------------------------------ | --------------------------------------------------------------------------------------------------------- |
| Dope-sheet layer row for `HoveredParameterHash`        | The existing "layer-hovered" background tint (the `UiColors.ForegroundFull.Fade(0.04f)` fill) is applied. |
| All of that parameter's curves in the curve pane       | Stay at full opacity; other expanded parameters' curves fade (e.g. ×0.4 alpha).                           |
| All of that parameter's curve overlays in the dope row | Same — stay prominent; peer parameters dim.                                                               |
| If `HoveredComponentBit != 0`                          | Within the hovered parameter, non-matching components fade too (both views).                              |
| Dope-sheet keyframe cluster at the hovered U           | Draw a soft outline (`UiColors.ForegroundFull`, 1 px × scale, no fill) around the icon.                   |
| Curve-pane keyframe with matching `UniqueId`           | Same soft outline on the `CurvePoint` icon.                                                               |

Keyframe emphasis uses the same outline treatment in both views so the link reads visually — same colour, same thickness, same shape class. This is the answer to the "how to show the keyframe link visually" open question: a shared outline on the matched icon in the other pane. Skip link-lines connecting the two panes — too noisy during normal editing.

**Important:** the hover fade must not apply when a drag is active or when a tooltip is open mid-interaction — freezing the current emphasis during drag avoids distracting the user mid-motion. Gate reads on `!ImGui.IsMouseDragging(0)` and similar where appropriate.

## Phase 4 — Selection sync + draggable splitter

**Selection sync**
- `DopeSheetArea.DrawKeyframe` currently marks `isSelected = SelectedKeyframes.Contains(vDef)`. Change to: selected if *any* of the parameter's curves has a selected `VDefinition` with the same `U` (within a tight epsilon — prefer exact match first, fall back to epsilon only if needed for float drift).
- Whole-stack drag from the dope sheet row already unions all component keys at that U into the drag set (existing behavior). No change required.
- Single-component drag in curve view → splits Us across components. Leave as-is (documented existing behavior).

**Splitter handle**
- 3-px horizontal handle between the two child windows. Cursor `SizeNS` on hover. Drag updates `CurvePaneHeightRatio` and clamps to `[0.15, 0.85]`. Persist across sessions via `UserSettings` — add a new float field.

## Phase 5 — Normalize view

Toggle in the curve pane chrome: `NormalizeCurveView`. Disabled by default.

**Per-curve normalization**
- Compute each curve's `[vMin, vMax]` from `GetVDefinitions()` values only. Tangent overshoots and post-extrapolation are ignored for range computation (can visually exceed [-1, 1] in render).
- If `vMax - vMin < ε` (e.g. `1e-6`), the curve renders flat at `V' = 0`.
- Otherwise map `V' = 2 * (V - vMin) / (vMax - vMin) - 1`.

**Canvas scale**
- While normalized, fix the pane's vertical scope to `[-1, 1]` with small padding. Overrides auto-fit.
- Toggling off restores whatever scope was in effect before (store a snapshot on enable).

**Interaction — full editing stays enabled (mandatory, parity with Blender)**

Vertical drag and tangent editing must work while normalized. Since each curve has its own `[vMin, vMax]` mapping to `[-1, 1]`, a single shared screen-Y delta translates to a different real-V delta per curve.

Implementation sketch:
- Cache `(vMin, vMax)` per curve at the start of each drag — do **not** recompute mid-drag. Otherwise the range itself shifts as the user drags, causing runaway/non-linear feedback.
- When normalized-mode drag is active, `UpdateDragCommand` receives the normalized `dv'` (from `InverseTransformPositionFloat` in `[-1, 1]` canvas space) and, per keyframe, applies `dV_real = dv' * (vMaxCached - vMinCached) / 2` using the keyframe's *own* curve's cached range. (Currently `UpdateDragCommand` applies a shared `dv` to all selected keyframes — this path needs to branch on normalize-mode to do per-keyframe scaling.)
- Tangent handles: same per-curve scaling. The tangent editing code in `CurveInputEditing` reads/writes tangent slopes in value-space; when normalized, slope edits are captured in normalized space then rescaled by the owning curve's `(vMax - vMin) / 2` factor before being stored.
- Edge case — flat curve (`vMax - vMin < ε`): drawn at V' = 0. Allow V-drag to bootstrap a real range: treat the curve as having an ephemeral range of `[value - 0.5, value + 0.5]` for the duration of the drag, so the user can pull a keyframe off the zero line. On drag-complete, recompute the real range for subsequent frames.
- Snap (`ValueSnapHandler`): snaps in real-V need to be transformed through the same per-curve scale before comparing against the normalized mouse position, or we disable V-snapping in normalize mode on first pass (simpler — revisit if users miss it).

**Keyframe + tangent render**
- Apply the V' transform to each keyframe's screen Y and to tangent handle endpoints so icons/handles land on the normalized curve.

## Testing

- Per phase, `dotnet build` on `Editor` project before reporting done.
- Manual verification steps live in [`.tests-manual/dopesheet-curve-expand.md`](../../.tests-manual/dopesheet-curve-expand.md). Extend that set — not this section — when adding new scenarios.

## Rollout order

Each phase is independently shippable and buildable. Phases 1–3 are the core feature; 4–5 add polish and the new major capability.

## Open questions (parking)

- Should the standalone `Modes.CurveEditor` view eventually go away? (User: "decide later".)
- Should U-drag of one vector component sync to sibling components? (User: "maybe".)
- Persist `ExpandedParameterHashes` across sessions? First pass: no.
