# Graph anchor tooltips show through covering windows

Ticket: #1075 — https://github.com/tixl3d/tixl/issues/1075
Size: —   Milestone: v4.2

## Problem
Tooltips for graph input/output anchors (and possibly other graph elements) appear even when the
graph element is covered by another window — the anchor is still "hovered" by distance test and the
tooltip pops on top of the covering window, for an element the user can't see.

## Affected code
`Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs`:
- Input anchor hover: line ~643 — `isInputHovered = Vector2.Distance(mouse, center) < 7*CanvasScale && state==Default;`
  Tooltip drawn at ~688 (vertical) and ~749 (horizontal).
- Output anchor hover: line ~895 — same distance-only condition; tooltip at ~923.
- Neither checks window occlusion. Other graph-element tooltips to audit: comment badge (~604),
  missing-input indicator (~1162).

Existing occlusion-aware patterns in the same area:
- `MagGraphCanvas.Drawing.cs:26` precomputes `IsHovered = ImGui.IsWindowHovered();`
- `ConnectionHovering.cs` already guards with `if (!context.View.IsHovered) return;` — the consistent fix.
- Node selection uses `ImGui.IsItemHovered(AllowWhenBlockedByPopup | AllowWhenBlockedByActiveItem)`.

## Proposed approach (candidate — must be verified in the running editor)
Gate the anchor hover conditions on the canvas view being the hovered window, reusing the existing
precomputed flag rather than a fresh ImGui call, e.g. add `&& context.View.IsHovered` to both
`isInputHovered` and `isOutputHovered` (matching `ConnectionHovering`). Audit the comment-badge and
missing-input tooltips for the same guard.

## Risks / side-effects
- Tooltip/hover is a frame-timing + window-stack interaction. Per AGENT_INSTRUCTIONS, this class of bug
  should be confirmed with log probes + a hot-reload repro, not guessed from static reading — which is
  why this is a plan, not an unattended edit.
- Picking the wrong predicate (plain `IsWindowHovered()` vs. the `AllowWhenBlockedByPopup` variant vs.
  the cached `View.IsHovered`) can over-suppress (tooltip never shows when a legit popup is open) or
  under-suppress (still bleeds through). Decide deliberately and test the covered-by-window repro.

## Open questions
- Should anchor tooltips be suppressed while *any* popup is open over the graph, or only when a real
  window covers the cursor? (Chooses between the flag variants above.)
- Are there other graph tooltips (op title, parameter rows) with the same distance-only hover?
