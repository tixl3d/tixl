# Plan: Scalable Window Layouts

## Goal

A saved window layout (shipped defaults, user layouts F1–F10, project sessions, the skill quest layout)
should keep its *intent* on any display:

- every split keeps its **proportion** (fractional units), e.g. skill quest graph : output stays roughly 1:1,
- the behaviour is the same for every split, so users can predict it. Today only the splits along the
  path to the graph keep fixed pixels, and nothing in the UI shows which splits behave that way.

## How ImGui resizes a dock tree

ImGui stores layouts in absolute pixels only: `[Window] Pos/Size` and `[Docking] Pos/Size/SizeRef`. There
is no percentage form, and ImGui.NET 1.91.6.1 doesn't expose the internal `DockBuilder*` API to set sizes
from code. When a dock node is given a new size, ImGui distributes it to the two children of each split
(`DockNodeTreeUpdatePosSize`):

1. If one child contains the **central node**, the *other* child keeps its pixel `SizeRef` and the central
   side takes the rest.
2. Otherwise the space is split in the ratio of the two `SizeRef`s, i.e. proportionally.

Our layouts make the graph window the central node, so every split on the path to the graph keeps the
other side at a fixed pixel size. Splits elsewhere (e.g. Output above Parameters) are already proportional.

Example, skill quest (`.Defaults/Layouts/layout12.json`): saved on a 1706×918 dockspace. The output column
(`SizeRef=739`) is the non-central side of the split with the graph, so it stays 739px wide. It was 43% of
the width when saved and is 29% on a 2560px-wide display at scale 1. Scaling by DPI alone keeps 739px at
scale 1, so it doesn't fix this.

## Rule

Every split is stored and restored as a **fraction** of its parent along the split axis:

```
childSize = savedFraction × parentSize
```

- `savedFraction` is the child's axis `SizeRef` divided by the sum of both children's axis `SizeRef`s,
  so it is exactly the ratio the user saw when saving.
- This overrides ImGui's central-node rule, so the graph is no longer the only area that absorbs size
  changes. All areas grow and shrink together.
- **Safety net only:** clamp each side to a minimum of `MinPanelSizeInPoints × T3Ui.UiScaleFactor`
  (about 120 points), taking the difference from the larger sibling. This is not a sizing rule. It only
  keeps a panel usable when a layout made for a big screen is opened on a small one.

| Case | Result |
|---|---|
| Bigger screen, same scale (1440p → 4K at 1×) | all areas 1.5× |
| Bigger screen, bigger scale (1440p@1× → 4K@2×) | all areas 1.5×; content is 2×, so panels show a bit less |
| Smaller screen (1440p → 1080p at 1×) | all areas 0.75×; minimum clamp on very narrow panels |
| Skill quest on 2560×1440 | output stays at its saved ~43% |

Layouts don't need to store a UI scale: the clamp uses the current one. A per-node override for a hard pixel
size can come later if one layout ever needs it.

## When the rule is applied

- **On layout load** (`LayoutHandling.ApplyLayout`, project session restore in `ProjectView.cs` ~525).
- **On viewport size change** (`LayoutHandling.UpdateAfterResize`, moving to a display of another size).
  Otherwise ImGui's own rule keeps everything except the graph at fixed pixels as soon as the app window is
  resized. Here the source is the live state, `ImGui.SaveIniSettingsToMemory()`, so splitters the user
  dragged during the session are kept.
  Apply once the resize has settled (no change for a few frames), not every frame of a drag.

## Implementation steps

1. No new layout fields are needed: fractions are derived from the `SizeRef`s already in the ini, and
   the saved dockspace size is there too (`DockSpace ... Size=`).
2. `ImGuiLayoutScaling.Rescale(string ini, Vector2 targetDockSpaceSize, IReadOnlySet<string> visibleWindowTitles)`
   in `Gui/Windows/Layouts/`:
   - Parse the `[Docking][Data]` tree (ID, Parent, SizeRef, Split, CentralNode) and the `DockId` of each
     visible `[Window]`.
   - **Ignore nodes without visible windows.** ImGui prunes them on load
     (`DockContextPruneUnusedSettingsNodes`), so their sibling takes the full parent. Example: the skill
     quest's library column, whose windows aren't in the layout.
   - Walk top-down from the target dockspace size. At each split along the axis (`Split=X`/`Y`), compute
     both children from the saved fraction, apply the minimum clamp, and write both axis `SizeRef`s. Also
     write the matching `Pos`/`Size` of docked `[Window]` entries, so the first frame doesn't jump.
   - Scale floating windows' `Pos`/`Size` by the dockspace size ratio, then clamp them into the viewport.
   - Leave every other key (`Selected=`, `HiddenTabBar=`, IDs) unchanged.
3. Call it where `Program.NewImGuiLayoutDefinition` is consumed (`WindowsUiContentDrawer.cs:162`, the only
   `LoadIniSettingsFromMemory` site, which also serves the Linux/Vulkan build). The ini is applied a frame
   after it is queued, so read the viewport size there.
4. Hook `UpdateAfterResize` to re-run it on the live ini once the size has settled.
5. Tests: unit tests for the rewriter with the real `layout0` and `layout12` inis (fractions kept, minimum clamp
   applied, pruned nodes ignored, other keys round-trip unchanged). Manual check: skill quest on 1706×918 and
   2560×1440, layout 0 at UI scale 1.0 and 2.0, and resizing the app window keeps all ratios.

## Status (2026-11-04)

Implemented: `ImGuiLayoutScaling` (Editor, linked into `Core.Tests` for `ImGuiLayoutScalingTests`),
`LayoutHandling.FitImGuiLayoutToDockSpace` at the ini load site, `LayoutHandling.KeepLayoutProportionsOnResize`
(refits 10 frames after the dock space size stops changing; it replaces `UpdateAfterResize`).

Deviation from the rule above: the SizeRef of the side that holds the central node is never written back by
ImGui and is stale (layout0's `0x0C` says 3140 inside a 2560 dock space). So the saved fractions come from
replaying ImGui's own distribution at the saved dock space size, not from the raw SizeRef ratio. On resize,
the replay uses the size the live layout was last fitted for, because the live ini already stores the new size.

Remaining: the manual check in `.tests-manual/scalable-layouts.md`, and a UI-scale change without a window
resize (menu bar height changes the dock space) also triggers a refit, which is intended but untested.

## Open questions

- Re-applying on resize overrides ImGui's own resize behaviour mid-session. If that feels jumpy while the
  app window is being resized, apply only once the size has settled (already planned) or only on release.
- Dragging a splitter between the graph and a panel still behaves like ImGui's central-node rule for the
  rest of that resize. That's fine, because the next save or resize re-reads the fractions from the live state.
