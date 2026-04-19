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

## Documentation and Manual Tests

- **User-facing docs live in `.help/`** and publish to `tixl.app/help/`. Developer / contributor topics stay on the GitHub wiki. Don't mix the two. See `.help/STYLE.md` for writing conventions and `.agentic/Plans/Plan_UpdateHelp.md` for the overall docs plan.
- When shipping a user-visible UI or behavior change, update the matching page under `.help/` in the same PR. If no suitable page exists, add one under the best-fitting section (`getting-started/`, `install/`, `using/`, `advanced/`, `contributing/`), or flag it in `Plan_UpdateHelp.md`.
- **Manual test sets live in `.tests-manual/`.** User-visible UI or behavior changes must also extend or add a test set in the same PR. Feature plans under `.agentic/Plans/` link to their test set instead of duplicating the steps. Stale tests are removed with the feature they covered. See `.tests-manual/README.md` for the file format. The long-term goal is an in-editor runner — see `.agentic/Plans/Plan_ManualTestRunner.md`.
- **Capture informal knowledge.** When the user explains something, shares a Discord thread, describes how they answered someone's question, or shows a meet-up clip — ask whether it belongs in `.help/`. If it's broadly useful, offer to draft a paragraph or a page from it. Raw source material (scripts, transcripts) lives in `.help/.src/` and isn't published.

## TiXL vs. Tooll3

TiXL is the current product (v4.x). Tooll3 (v3.x) is the legacy predecessor — a large portion of v4 is a rewrite. Don't write new docs or features targeting Tooll3; treat remaining Tooll3 references in code as historical and prefer removing them over updating them unless there's a concrete migration use case.

## Debugging Runtime Behavior with Log Probes

The editor supports hot reload, so adding temporary `Log.Debug(...)` / `Log.Info(...)` lines to test hypotheses is cheap and **welcome**. For non-trivial runtime bugs — UI timing, layout/frame-order interactions, state flowing through multiple canvases or editors — don't guess fixes from static code reading. Instead:

1. State the hypothesis and the specific value distinction that would confirm vs refute it.
2. Drop 2–3 targeted `Log.Debug(...)` lines into the suspected code paths.
3. Ask the user to hot-reload and perform the specific interaction that triggers the bug.
4. Read the log tail (see below) and let the data confirm or kill the hypothesis before writing the fix.
5. Fix only after the log settles the question.

**Log location.** Each editor run writes a timestamped log to the user's roaming app data, under a version-specific subfolder — on Windows:

```
%APPDATA%\TiXL<major>.<minor>\Log\<YYYY_MM_DD_HH_MM_SS_mmm>.log
```

Concretely that resolves to something like `C:\Users\<user>\AppData\Roaming\TiXL4.2\Log\2026_04_19_18_00_12_560.log`. Username and version (`4.2`, `4.3`, …) vary — list the `Log/` directory first and pick the most recent file (the active log is the newest by modification time; the editor is typically still running and writing to it).

Reading the tail of an active log file is safe: use the standard file-read tooling with an `offset` near the end, or count lines first and read the last N. Don't print the whole file — logs grow fast (hundreds of KB in a single session).

**Cleanup.** Remove temporary `Log.Debug` probes once the bug is understood and fixed, unless they have lasting diagnostic value worth keeping.

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

