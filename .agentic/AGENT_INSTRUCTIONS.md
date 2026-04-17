# Agent Instructions for TiXL

## Mission
Contribute to **TiXL** with a focus on:
1. Correctness
2. Realtime performance
3. Code consistency with the existing codebase

## References
Use these sources first when behavior or conventions are unclear:
- Main documentation: https://github.com/tixl3d/tixl/wiki
- How TiXL works: https://github.com/tixl3d/tixl/wiki/help.HowTixlWorks
- Operator conventions: https://github.com/tixl3d/tixl/wiki/dev.OperatorConventions

If documentation and implementation differ, follow local code patterns in the affected project unless a task explicitly asks for broader refactoring.

## Solution Structure (Key Projects)
- `Core/` - Shared functionality between Editor and Player
- `Editor/` - Main user interface
- `Player/` - Exported project playback
- `Operators/Lib/` - Primary operators
- `Operators/TypeOperators/` - Operators for creating base types
- `Operators/Examples/` - Example operators and setups

Keep changes scoped to the smallest project boundary that solves the task.

## Realtime Performance Constraints (Critical)
TiXL uses realtime rendering and Dear ImGui. UI refresh is synchronized with output rendering, so slower frame times reduce UI responsiveness.

For methods called once per frame (e.g. operator update, Editor draw methods):
- Avoid heap allocations
- Avoid LINQ
- Prefer explicit loops and reusable buffers

Allocations are acceptable for explicit user-triggered actions.

## State and Resource Handling
- Avoid storing long-lived direct references to instances/resources.
- Prefer storing and resolving by `Guid`.
- Be careful with stale references across reloads and graph changes.

## Operator Rules
When changing operators, follow:
- https://github.com/tixl3d/tixl/wiki/dev.OperatorConventions

Also:
- Keep operator evaluation paths allocation-free
- Match existing naming and slot conventions
- Avoid hidden side effects unless explicitly intended

## Code Formatting and Style
- Put `return` statements on their own line (not inline after `if`)
- Place private fields and private enums at the end of classes
- Prefix private fields with `_`
- Prefer slightly longer, descriptive names when clarity improves (e.g. `faceIndex` over `i`)

## Line Endings (Important for Bulk Edits)
The repo has **mixed line endings**: most `.cs` files use CRLF, but some are LF.
There is no `.gitattributes` enforcing a single convention, and `core.autocrlf` is `false`.

When writing scripts (Python, sed, etc.) to batch-rewrite many files, this is the
single biggest source of noisy diffs. Naive read/write loops normalize everything
to LF and produce a "every line changed" diff that buries the actual logic change.

Rules for any bulk-edit script:
1. **Read in binary mode** (Python: `open(path, 'rb')`) — do NOT use `read_text` or
   text-mode reads, which silently strip `\r`.
2. **Detect each file's existing line ending** before writing. If the file contains
   `\r\n`, write it back with CRLF; otherwise LF. Per-file, not per-repo.
3. **Write in binary mode** (`open(path, 'wb')`) with the bytes you produced — do
   NOT pass `newline=''` to a text-mode open and expect it to preserve anything.
4. After the script runs, sanity-check with `git diff --shortstat` before committing.
   If the line count is suspiciously high relative to the logic change, you almost
   certainly munged line endings — fix them in the working tree, then `commit --amend`.
5. New files you create may use either convention; CRLF is the more common default
   in this repo, but matching nearby files is preferred.

## UI Implementation Guidelines

TiXL's editor is Dear ImGui plus custom widgets, rendered every frame at the output refresh rate. The same performance rules apply as elsewhere (no allocations in the hot path), with extra guidelines below. The style reference for non-trivial UI is `Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs` — when in doubt, mimic the patterns there. The Legacy Graph code is **not** a reference and can be ignored.

### Before you write UI code
- Ask clarifying questions about behavior and visuals first. UI iterations are expensive; a quick round-trip on the design beats guessing.
- Check for an existing helper in `CustomComponents` and `FormInputs` before writing a new widget.
- Check `Icons` for a baked-in glyph before drawing your own — atlas icons render in a single draw call.

### Theming & colors
- Never construct `new Color(...)` in draw code. Always use `UiColors.*` (or `ColorVariations.*.Apply(typeColor)` for type-derived colors) so themes work.
- For alpha/shading, use `color.Fade(alpha)` (1 = fully opaque). Never mutate `.A` directly.
- For state-based blending (e.g. active vs idle), use `Color.Mix(a, b, t)`.

### Scaling
- Apply `T3Ui.UiScaleFactor` to every layout constant (sizes, paddings, offsets). Pixel literals break on high-DPI displays.
- On the MagGraph canvas or any zoomable surface, combine factors in this order: `value / T3Ui.UiScaleFactor * CanvasScale` — UI-scale first, then canvas zoom.
- Threshold-cull expensive detail at low zoom. MagGraph skips labels below `CanvasScale 0.25f`, thumbnails below `0.2f`, etc.

### Fonts
- Only use values from the `Fonts` enum (`FontNormal`, `FontSmall`, `FontBold`, `FontLarge`). Target mix: Normal ~70 %, Small ~20 %, Bold ~5 %, Large <2 %.
- If you change `Fonts.*.Scale` for a local effect, reset it to `1` before returning from the draw method — the scale leaks into sibling draws otherwise.

### Draw lists, z-order, hit-testing
- Prefer a single draw list. Z-order is controlled by call sequence, not ImGui channels.
- Only split the draw list (channels) when overlapping elements need independent z-order *and* the top one must receive clicks. Merge immediately after — extra channels cost draw calls.
- For overlapping clickable elements, emit the topmost `InvisibleButton` last so it wins the hit test. If that is not feasible, split channels.
- Avoid excessive `PushClipRect` — each clip region adds draw calls. Clip only when drawn content would genuinely overflow.

### Performance in draw methods
- No allocations in per-frame draw code (see "Realtime Performance Constraints" above). No LINQ, no closures, no `params` arrays.
- Reuse static buffers for repeated small geometry (`static Vector2[] _points = new Vector2[5]`).
- Pass hot data by `ref` (anchor points, line rows) rather than by value.
- Keep tooltips inside `if (isHovered) { ... }` blocks so `BeginTooltip` isn't called every frame for invisible widgets.
- Drive pulsing/blinking from a single global time source (e.g. a shared `Blink` sine wave) rather than per-element timers.

### Undo/redo
- Any mutation to symbol/animation/graph data must go through a command (`UndoRedoStack.AddAndExecute(...)`). Direct mutations break undo and often break save state too.
- For drag interactions: construct the command on drag start, update its target values during the drag, push to the undo stack on drag complete — not on every mouse-move.

## Review and Quality Expectations
- Point out obvious problems, misleading code, incorrect implementations, and typos
- Fix spelling mistakes in touched comments on the fly
- Add parameter documentation only when parameter purpose is not obvious from the name

## Change Workflow
### Before editing
1. Check whether the target code runs every frame
2. Read nearby code for local conventions
3. Confirm correct project boundary (`Core`, `Editor`, `Operators`, etc.)

### During editing
1. Keep diffs minimal and targeted
2. Avoid opportunistic refactors unless requested
3. Preserve behavior unless task explicitly changes behavior
4. Respect existing contracts and nullability assumptions

### After editing
1. Build/check impacted projects
2. Verify no obvious regressions in hot paths
3. Call out assumptions and risks

## Review Checklist
- [ ] No new allocations or LINQ in per-frame paths
- [ ] No new long-lived direct references where `Guid` should be used
- [ ] Operator changes follow operator conventions
- [ ] Style rules above are followed
- [ ] Diff remains focused and minimal
- [ ] Any frame-time/UI-responsiveness risk is explicitly mentioned

## Communication Expectations
When reporting changes:
- Explain what changed and why
- Mention performance impact (or confirm none expected)
- Highlight tradeoffs and residual risks
- Keep follow-up suggestions actionable

