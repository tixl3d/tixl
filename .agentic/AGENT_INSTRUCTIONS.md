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

**Keep `Core/` changes minimal.** `Core` is shared by the Editor, the Player, and every operator package, so additions there have the widest blast radius and the longest lifetime. Prefer putting new types in `Editor/` (or the relevant operator project) unless the functionality is genuinely needed by `Player` or operators. "It's settings-ish" or "it might be reused" is not enough of a reason. When in doubt, put it in `Editor/` and ask before promoting it to `Core`. (Example: editor-only UI state like the welcome-popup version marker belongs in `Editor/`, not `Core/Settings/`.)

## Realtime Performance Constraints (Critical)
TiXL uses realtime rendering and Dear ImGui. UI refresh is synchronized with output rendering, so slower frame times reduce UI responsiveness.

For methods called once per frame (e.g. operator update, Editor draw methods):
- Avoid heap allocations
- Avoid LINQ
- Prefer explicit loops and reusable buffers

Allocations are acceptable for explicit user-triggered actions.

Apply these rules where they matter — code that actually runs every frame and shows up in a profile. **Don't speculatively micro-optimise** cold paths: precomputed reciprocals (`inv = 1f / x`), hand-unrolled loops, or hoisting obvious divisions out of a small loop often hurt readability for a perf delta nobody will ever notice. Write the division the way you'd describe the math; reach for the optimised form only when a measurement says to.

## State and Resource Handling
- Avoid storing long-lived direct references to instances/resources.
- Prefer storing and resolving by `Guid`.
- Be careful with stale references across reloads and graph changes.

### Undo / redo commands

`ICommand` implementations live longer than any single graph state — they sit on the undo stack until the user closes the project (or further) and need to survive operator-package hot reloads, which recreate `Instance` objects under the same `Symbol`.

- **Do not store `Instance` references** in command fields. Store the relevant `Guid` (`compositionOp.Symbol.Id`, `instance.SymbolChildId`, etc.) and resolve at call time via `SymbolUiRegistry.TryGetSymbolUi(...)` / `Symbol.Children` / similar.
- **Do not store `Symbol` or `SymbolUi` references** either — same reload exposure. Re-resolve from the registry each `Do`/`Undo`.
- Resolution helpers should be defensive: if the registry no longer has the symbol (project closed, package unloaded), log a warning and silently no-op the command. Don't throw — the undo stack may invoke the command long after the symbol's lifetime.
- `SymbolChild` lifetimes are tied to the parent `Symbol`, so storing a `SymbolChild` works as long as the parent symbol is still registered. Resolving from the parent symbol's `Children` dict is more robust than caching the child directly.
- Pure data captured for undo (clip ranges, parameter values, etc.) can be stored by value — those snapshots don't depend on graph instance lifetime.

The canonical references are [`AddSymbolChildCommand`](../Editor/UiModel/Commands/Graph/AddSymbolChildCommand.cs) and [`DeleteSymbolChildrenCommand`](../Editor/UiModel/Commands/Graph/DeleteSymbolChildrenCommand.cs) — both store `_parentSymbolId` / `_compositionSymbolId` and look up the live symbol per call. Some older commands (`MoveTimeClipsCommand`) still hold `Instance` references; treat those as latent bugs to fix when next touching them, not patterns to copy.

## Operator Rules
When changing operators, follow:
- https://github.com/tixl3d/tixl/wiki/dev.OperatorConventions

Also:
- Keep operator evaluation paths allocation-free
- Match existing naming and slot conventions
- Avoid hidden side effects unless explicitly intended

### Operator descriptions

An operator's Description field (edited in the TiXL editor) is the source for both the editor's help panel and
the generated `.help/docs/operators/**` page — so write descriptions **in the editor**, not in the generated
`.md` (hand-edits to the `.md` are overwritten on regeneration). Follow the description conventions from the
wiki ([dev.OperatorConventions](https://github.com/tixl3d/tixl/wiki/dev.OperatorConventions)):

- **The first line is a one-line summary** that conveys what the operator does and nothing more, followed by a
  **blank line** before any further detail. Descriptions render as Markdown, so the summary needs that
  paragraph break (a single newline won't separate it). Lists, search, and tooltips use the first line as the
  short summary, so it must stand on its own.
- Put further detail in the following paragraphs. List synonyms under an `AKA:` line, and subjective usage
  advice under a `Tips` section.
- Write the description body in the third person (avoid "you" / "I"); the `Tips` section may use second person.

## Code Formatting and Style
- For multi-line string literals (tooltips, log messages, help text), prefer a **raw string literal** (`"""…"""`) over `"line\n" + "line\n"` concatenation. It reads as the text it produces, avoids stray `\n`/`+` noise, and the multi-line shape is visible in source. Reach for concatenation/interpolation only when lines are built conditionally.
- Put `return` statements on their own line (not inline after `if`)
- **Brace every branch of an `if` / `else` / `else if` chain**, even single-statement branches — a brace-less `else if` (especially with a comment under it) reads poorly and is easy to misedit. A lone `if (x) foo();` with no `else` may stay brace-less (matches the prevailing idiom; don't mass-reformat existing ones).
- Order class members public-first → private, with private fields at the very end. Nest helper types (structs, enums) used only by the owning class inside it, at the top.
- Prefix private fields with `_`
- Prefer slightly longer, descriptive names when clarity improves (e.g. `faceIndex` over `i`)
- When separating concerns, consider splitting pure data/state from drawing/IO into distinct classes (`RollingMetric` + `MetricGraphView` is a reference example). Useful when the data class has non-editor consumers, but don't force it when there's only one caller.

## Comments

Comments live forever. They must earn their place. Anyone reading the file six months from now should still benefit from every line of comment in it.

- **No session context in source.** Do not reference Sentry issue IDs (`see Sentry TOOLL3-XYZ`), plan filenames (`see Plan_FooBar.md Phase 2`), commit titles, dates, or "this fix / the previous commit." Those belong in commit messages and plan files, not in code. A reader without the conversation has no use for them — they're noise that erodes signal.
- **Comment why, not what.** If the code says `DataBuffers?.Dispose()`, explain *why* `DataBuffers` can be null in this branch — don't explain what `?.` does.
- **One short line beats a paragraph.** `// MidiIn callbacks fire on per-device threads; serialize writes here.` is more useful than a five-line block restating the same. **Cap inline comments at ~2 lines.** A longer block enumerating every reason/edge case/alternative (e.g. "case-insensitive because X… strip by length not Replace because Y… which would also Z…") is a smell — keep the single most important *why* and drop the rest, or move it to xmldoc. If you find yourself writing a third sentence, ask whether the function name or xmldoc should carry it instead.
- **No procedural narration.** "Register X as the very first thing because if we don't, Y will happen later" — drop. The order of statements is visible; the *reason ordering matters* is the only useful note, and it can be one line.
- **xmldoc is for API contracts.** Use `<summary>` to describe what a member does and when callers should use it. Don't journal bugs of the day or fix history there.
- **Preserve good existing comments.** Architectural notes, non-obvious invariants, "this looks wrong but it's correct because X" — leave them. The rule is about *not adding* noise, not about scrubbing the file.

## Encapsulation and Visibility
- **Default to `private`.** Raise visibility (`internal`, `public`) only when a member is actually read or written from outside the declaring type. "Mirroring a nearby class" or "might be useful later" is not a reason — unnecessary public surface area is a long-term review tax.
- **Prefer constructor parameters over public setters** for values needed at construction time. `new Foo(x, y)` is always better than `new Foo { X = x, Y = y }` unless the setter is also used later. Command classes in `Editor/UiModel/Commands/` are a common place this goes wrong — initialize `_newValue` / `_originalValue` in the constructor and keep them `private readonly`.
- Don't copy an existing class's visibility blindly; if the nearby class exposes something as `public` without a caller, that's a bug to not propagate, not a pattern to match.

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

**Status-color meaning.** The `UiColors.Status*` hues carry a consistent meaning across the editor — use the matching one so indicators read the same everywhere:

| Hue | Color | Meaning |
| --- | --- | --- |
| Orange | `StatusAnimated` | Related to time (animation, keyframes, playback) |
| Blue | `StatusAutomated` | Driven or linked (input connection, expression) |
| Green | `StatusControlled` | Controllable by snapshots or other UI features |
| Magenta | `StatusAttention` | Needs attention (errors, recording, muted audio) |

### Scaling
- **Every pixel literal must be multiplied by `T3Ui.UiScaleFactor`.** No exceptions. This includes widths, heights, paddings, gutters, indents, font sizes, marker radii, draw-list line thicknesses, `SameLine` spacing, `Dummy` sizes, `SetCursorPosX/Y` offsets, manual cursor adjustments, anything passed into `new Vector2(...)` for ImGui. Pixel literals render correctly on the developer's monitor and then collapse or balloon on every other display — high-DPI laptops, 4K monitors, users with non-100% UI scale.
  - Wrong: `ImGui.Dummy(new Vector2(0, 16))`, `SameLine(0, 6)`, `ImGui.GetWindowDrawList().AddCircleFilled(p, 2, color)`.
  - Right: `ImGui.Dummy(new Vector2(0, 16 * T3Ui.UiScaleFactor))`, `SameLine(0, 6 * T3Ui.UiScaleFactor)`, `AddCircleFilled(p, 2 * T3Ui.UiScaleFactor, color)`.
  - The only things you can leave unscaled are values that *already* came from ImGui (`GetFrameHeight()`, `CalcTextSize(...)`, `GetWindowWidth()`, item-rect sizes) — those are already in scaled pixel space.
- Helpers that already scale internally — `FormInputs.AddVerticalSpace(n)` — take an *unscaled* number and apply the factor themselves. Don't double-scale: pass `5`, not `5 * T3Ui.UiScaleFactor`.
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

### Prefer existing helpers over hand-rolled ImGui

TiXL has accumulated a set of small helpers in `Editor/Gui/Styling/CustomComponents*.cs`,
`Editor/Gui/Input/FormInputs.cs`, `Editor/Gui/Styling/Icons.cs`, and
`Editor/Gui/Hub/ContentPanel.cs` that already encode the editor's look-and-feel.
Use them before writing new patterns. New `PushFont` / `PushStyleColor` /
`TextUnformatted` blocks for things that already have helpers are a review
flag — they drift from the theme as it evolves.

Quick map of what to reach for:

- **Styled text** — `CustomComponents.StylizedText(text, font, color)` instead
  of `PushFont` + `PushStyleColor` + `TextUnformatted` + two pops. Use it for
  every short, non-wrapped label that needs a non-default font or color.
  For wrapped paragraphs you still need `TextWrapped` (StylizedText uses
  `TextUnformatted`).
- **Section panels with a title bar** — `ContentPanel.Begin(title, subtitle,
  drawTools)` / `ContentPanel.End()`. Handles the indent, title font,
  subtitle, and optional right-side tool slot. Don't hand-roll a header row
  with `FormInputs.AddSectionHeader` + manual `PushFont` calls unless the
  layout genuinely doesn't fit the panel shape.
- **CTA / outcome buttons** — `CustomComponents.DrawCtaButton(label, icon,
  textColor, bgColor, borderColor)` or the `ButtonStates` overload. Picks
  up the project's CTA proportions and the FontLarge text style.
- **Right-aligned tool clusters** — `CustomComponents.RightAlign(itemWidth)`
  instead of `SetCursorPosX(GetWindowWidth() - … - WindowPaddingOverride.X)`.
  Cleaner and stays correct if padding changes.
- **Icon buttons** — `CustomComponents.IconButton(Icon.X, size, state)` for
  clickable tool icons; use the enum, not character literals or per-call
  `PushStyleColor`. See "Tool icons" below for the state semantics.
- **Inline icons in text rows** — `Icon.X.DrawAtCursor()` or
  `Icon.X.DrawAtCursor(color)`. For ad-hoc positioning use
  `Icons.DrawIconAtScreenPosition(...)`.
- **Context menus** — `CustomComponents.ContextMenuForItem(drawItems, title,
  id)` with `CustomComponents.DrawMenuItem(...)` rows and
  `CustomComponents.DrawMenuGroupLabel(...)` section headers. See "Context
  menus" below.

### Context menus

Right-click / popup menus must use the `CustomComponents` menu API for a
consistent look — **do not hand-roll** `ImGui.BeginPopupContextItem` +
`ImGui.MenuItem` / `ImGui.Separator` / `ImGui.TextDisabled` blocks (they drift
from the theme: wrong padding, no checkmark column, mismatched fonts).

- Wrap the menu in `CustomComponents.ContextMenuForItem(drawItems, title, id)`.
  It handles the drag-guard, popup styling, and the small gray `title` header.
- Emit rows with `CustomComponents.DrawMenuItem(id, label, …)` — it has a
  checkmark column (`isChecked:`), an optional icon column, and a right-aligned
  shortcut. Group rows under `CustomComponents.DrawMenuGroupLabel(label)`
  instead of `Separator` + `TextDisabled`.
- `ContextMenuForItem` runs every frame for the item, so pass the `drawItems`
  callback as a **static method group**, not a lambda, whenever the menu body
  reads only static state — a capturing lambda allocates a closure each frame.
  Reach for a lambda only when the menu genuinely needs per-instance locals.

### Menu and command labels

**TiXL uses Title Case for every clickable command label** — menu items, submenu headers, menu bar
entries, buttons, and context-menu rows. This follows the tradition of the tools TiXL users have open
next to it (After Effects, Resolve, Blender, Unity) and Apple's HIG. It deliberately differs from
Microsoft's modern sentence-case guidance and from web-era tools like Figma; consistency within TiXL
matters more than matching any single external vendor.

Style guides disagree on the details (see [Title case](https://en.wikipedia.org/wiki/Title_case) for a
comparison). TiXL follows the **lowercase-short-prepositions** variant used by Chicago, MLA and Apple —
**not** AP/APA, which capitalize four-letter prepositions. The two differ only on `with`, `from`, `into`
and `over`; we write `Sync with Selection`, not `Sync With Selection`.

The rule, so short words don't become a judgement call:

- Capitalize the **first and last word**, always.
- Capitalize nouns, verbs, adjectives, adverbs, and pronouns — including short ones (`Is`, `Be`, `Set`).
- **Lowercase** articles (`a`, `an`, `the`), coordinating conjunctions (`and`, `but`, `or`, `nor`),
  and prepositions of **four letters or fewer** (`at`, `by`, `in`, `of`, `on`, `to`, `as`, `for`,
  `from`, `with`, `into`, `over`) — unless they are the first or last word.
- Prepositions of **five letters or more** are capitalized: `Under`, `Above`, `Between`, `Through`.
- A particle in a phrasal verb is part of the verb, so it **is** capitalized: `Set Up`, `Clean Up`,
  `Zoom In`. Contrast `Cut at Time` (preposition) with `Pin to Output` (preposition).

Examples: `Cut at Time`, `Clear Time Stretch`, `Select Following Clips`, `Reset to Default`,
`Combine into New Type...`, `Open in Explorer`, `Sync with Selection`, `Set as Main Soundtrack`.

Two related conventions:

- **Trailing `...` means "this needs more input"** — the command opens a dialog, popup, or picker
  rather than acting immediately. `Rename...` opens a dialog; `Rename` renames inline. Do **not** put
  `...` on submenu headers (the chevron already says there's more) or on section labels.
- Use three periods (`...`), **not** the single-character ellipsis `…`. The font atlas only rasterises
  a fixed glyph range, and `…` is outside it.

Section headers (`DrawMenuGroupLabel`, `HintLabel`) are labels too, so they also use Title Case —
just without the trailing `...`: `Interpolation`, `UI Elements`, `Input Settings`.

Actual prose keeps sentence case: tooltips, help text, warnings, confirmation questions, and
parameter descriptions are sentences, not commands.

### Tool icons

`CustomComponents.IconButton` is the one canonical tool icon. Don't hand-roll
icon buttons (no `InvisibleButton` + manual hover rect, no per-call
`PushStyleColor(Button, …)` wrappers) — that fragments the look. The helper is:

- **Transparent background by default**, with a *subtle* rounded hover. The
  plain ImGui button-hover color is too dominant under a transparent icon, so
  the helper uses a muted one. Don't reintroduce opaque backgrounds.
- Tinted by **`ButtonStates`** (the icon color carries the meaning, not a
  background):
  - `Default` — slightly faded; the resting look for almost every tool icon.
  - `Emphasized` — slightly brighter; for the one primary/active action in a row.
  - `Disabled` — barely visible **and not hoverable** (no hover background), but
    the tooltip still shows. Use for actions that aren't currently available;
    gate the click yourself (`IconButton(…, Disabled)` still returns the click).
  - `Activated` — the only state with a filled background, to read as "on"
    (toggles like loop mode, show-gizmos, console auto-scroll).
  - `NeedsAttention` — temporarily important and easy to overlook (audio muted,
    currently rendering); a destructive icon only needs this on hover.
- For an icon whose color isn't a state (type-colored, status-colored) use the
  `IconButton(Icon, size, Color)` overload — still transparent with the same
  hover. `TransparentIconButton` is a backwards-compatible alias and forwards to
  `IconButton`; prefer `IconButton` in new code.
- **Toolbars** (playback transport, output toolbar) are the exception: their
  buttons need filled backgrounds to tile into a continuous bar. Wrap the
  toolbar's icon cluster in `CustomComponents.PushToolbarIconBackground()` /
  `PopToolbarIconBackground()` — every icon-button variant inside then keeps a
  background. Don't reintroduce per-button background pushes.

### Don't draw UTF-8 glyphs in place of icons

If a `✓` / `✗` / `⚠` / `→` would do, **use the corresponding `Icon` enum value
plus the helpers above**. Two reasons:

1. The font atlas only rasterises a fixed glyph range
   (`Editor/UiContentDrawing/UiContentUpdate.cs`). Characters outside it
   render as the missing-glyph rect (`?`). Don't expand the atlas just to
   sneak in a single literal character.
2. Drawing through `Icons.cs` keeps everything baseline-aligned with the rest
   of the editor, theme-coloured, and consistent across DPI.

If the icon you need isn't in the `Icon` enum (`Editor/Gui/Styling/Icons.cs`)
**ask the user to add it** rather than fall back to a character. A draw-list
primitive (filled circle, rect) is acceptable as a *temporary* placeholder
when no icon exists yet — call it out in the response so it can be replaced
later.

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
- **Manual test sets live in `.tests-manual/`.** User-visible UI or behavior changes must also extend or add a test set in the same PR. New sets must include `added:` (ISO date) and `added-in-version:` (TiXL `major.minor`) in their frontmatter — the in-editor runner sorts by recency on these. Feature plans under `.agentic/Plans/` link to their test set instead of duplicating the steps. Stale tests are removed with the feature they covered. See `.tests-manual/README.md` for the file format. The in-editor runner is the **Guided Feature Tests** window.
- **Capture informal knowledge.** When the user explains something, shares a Discord thread, describes how they answered someone's question, or shows a meet-up clip — ask whether it belongs in `.help/`. If it's broadly useful, offer to draft a paragraph or a page from it. Hand-authored source material (video scripts, drafts) lives in `.help/.src/`; regenerable machine output (e.g. meet-up video transcripts, extracted audio) lives in `.help/.tmp/` (git-ignored). Neither is published.

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

**Always pass `this` as the second argument inside operator code.** `Log.Debug(...)` / `Log.Warning(...)` / `Log.Error(...)` accept an instance reference via the `params object[]` arg list — passing `this` makes the log line clickable in the editor's Console window so the user can jump straight to the offending operator instance. Skip it only in `static` contexts (static constructors, static probes) where no instance exists.

```csharp
// Inside an operator (instance context):
Log.Warning($"SwiftCam: capture failed - {e.Message}", this);

// Static context — no `this`:
Log.Debug("SwiftCam: pre-loading native DLL");
```

**Use `Log.Warning` for user-actionable problems** (failed Open, disconnects, frame timeouts, unrecoverable state) and `Log.Debug` for one-time lifecycle traces (start/stop, first frame, reconnect triggered). High-frequency or per-frame trace probes belong behind a `LogMessages` (or equivalent) input toggle — default off — so the log stays readable in steady state.

## Review and Quality Expectations
- Point out obvious problems, misleading code, incorrect implementations, and typos
- Fix spelling mistakes in touched comments on the fly
- Add parameter documentation only when parameter purpose is not obvious from the name

## Comment Restraint

Comments are not free — every line a reader scans is a line that has to be kept honest as the code evolves. Restraint matters more than verbosity. Optimise for: a future reader skimming a method understands *what it does* in seconds, and only sees a comment when the *why* isn't obvious from the code.

**Default to no comment.** Reach for one only when:

- The *why* is non-obvious from the code (an invariant, an ordering constraint, a workaround tied to an external bug, a perf-vs-clarity tradeoff). Spell out the why; the what is already in the code.
- A method's name doesn't fully convey its contract (XML `<summary>` — one short sentence).
- A back-compat handler reads a legacy field shape (briefly note it's back-compat).

**Don't write:**

- Multi-paragraph rationales reconstructing the debugging session that led to the current code ("we tried X first but ImGui hit-tested through child rects so we did Y …"). The reader needs the current invariant, not the history. If the history genuinely matters, put it in the PR description — not the comment.
- **Any reference to a plan in a code comment** — `.agentic/Plans/`, a `Plan_*.md` filename, "see the plan", "open question #N", and the like. This applies to *every* comment, not only phased work: plans get archived, renamed, and rewritten, so the pointer rots silently and sends the reader chasing a doc that may be gone. State the lasting *why* inline instead. Cross-references to other *code* (a class, an op, a sibling file's behaviour) are fine; pointers to plan documents are not.
- Comments restating what the next line of code says (`// increment counter` above `i++`).
- "Phase 4 will replace this with …" markers — see [Comment Hygiene Across Phased Work](#comment-hygiene-across-phased-work) below. Sweep them on the wrap-up commit.
- XML docs that re-describe parameter types or the obvious shape of a method (e.g. `<param name="path">The path</param>`). Per the Review Expectations above, parameter docs are for non-obvious purpose only.

**On revisions:** if you're touching a section with a sprawling block comment you wrote earlier in the session, trim it. The first draft often over-explains because every detail felt important at the time; once the code stabilises, most of those details are noise. Aim for the comment-to-code ratio you'd expect in well-maintained open-source library code — sparse and load-bearing.

## Comment Hygiene Across Phased Work

When work is split into phases (typically tracked under `.agentic/Plans/`), it is fine — sometimes useful — to leave transitional comments in code during the work:

- "Phase B of `Plan_X.md` will replace this with …"
- "Pre-rename projects used the property name `Foo`."
- "Behavior preserved during the transition; full unbundling deferred."

These comments help the next reviewer (or the same agent on the next turn) understand why the code currently looks half-done. **But they are temporary scaffolding, not documentation.** Once the feature lands they become agent-generated cruft: a future reader has to dig into a plan doc that may itself be archived to understand what the code is saying.

**Rules:**

1. **During phase work**, transitional comments referencing phases, plans, or prior names are acceptable as long as they justify *current* code shape or flag *imminent* follow-up.
2. **In the final phase or a dedicated cleanup commit at the end of a feature**, sweep them out. For each transitional comment, choose one:
   - **Delete** if the reasoning is now obvious from the code itself.
   - **Rewrite** to remove the phase / plan / "post-rename" framing, keeping only the lasting *why* (e.g. "Streams stay loaded until the composition closes — avoids thrash when scrubbing across a TimeRange boundary." — no mention of "DiscardAfterUse was removed in Phase B…").
3. **Don't reference `.agentic/Plans/` from production code.** Plans are working documents; they may be archived or rewritten. Code comments that point at them go stale silently. References from other plans, agent instructions, or tests are fine — those documents track their own lifecycle.
4. **Back-compat readers** (e.g. JSON migration paths reading old field names) are an exception: keep them, and explain that they're back-compat handlers for old saved data, without naming a phase or plan. The migration is permanent code now, not scaffolding.

When picking up a feature mid-phase, treat existing transitional comments as a working state of the prior author's thinking — don't aggressively prune them until the feature is being wrapped up.

## Interface stability for new features

Before locking in a data format, wire type, or operator parameter set — typically right before a feature ships — pause and audit. Once data lands on user disks and ops land in user projects, every shape change carries migration cost. Optional extension points are cheap today; forced migrations are expensive forever.

**The audit:**

1. **List the next 6 months of plausible use cases**, even speculative ones. For a recording feature, that might mean: scripting capture, transcribing audio, importing MIDI files, driving keyframes, exporting CSV, editing in a spreadsheet, procedural writes. Don't filter for likelihood — listing surfaces what's *expressible* vs what would force a redesign.
2. **For each, identify what would force a migration.** If a use case can be served by adding a new op, a new file, or an optional field, the format is safe. If it would force renaming an existing field, splitting a type, or breaking the wire shape, the format is too narrow.
3. **Prefer additive flexibility:**
   - Optional metadata bags (JSON objects, dictionaries) at every level where future fields might land — root, channel, etc. Old readers ignore them; new readers use them when present.
   - Version fields on serialized formats. Lets a future reader say "v3? I only know v2 — load what I can, warn about the rest."
   - String discriminators over closed enums where the discriminator might grow (event kinds, source types, AssetType identifiers).
   - Reserved-but-unused values, documented in code comments, so contributors don't accidentally collide with planned ones.
4. **Avoid:**
   - Per-event fields that multiply with cardinality. Hoist discriminators / metadata to the lowest level where the value is constant. A duration-type that's the same for every event on a channel goes on the channel, not the event.
   - Subclass proliferation as the extension mechanism. Each new subclass needs serializer support, deserializer support, exhaustive switch handling, and rules about what a writer of v3 should do when an old v2 reader encounters it. Optional fields on a single class scale better.
   - Coupling unrelated concerns into one field. Value type (float / string / int) is orthogonal to event duration type (tick / interval). Don't fold them.

The cost of skipping this audit shows up months later as JSON migration scripts, op-input renames, and broken saved projects.

## Feature retrospective: overlaps and low-hanging fruit

When a major feature wraps — the last planned phase ships, or the user signals "this is shippable" — pause before moving on and surface two things explicitly in the next message:

1. **Overlap with other in-flight or planned features.** What did this work touch that another upcoming feature also touches? Did we build infrastructure (a cache, a bus, a value type, a UI hook) that something else on the roadmap could now lean on instead of duplicating? Flag the overlap and link it to the relevant plan doc.
2. **Low-hanging fruit that's now cheap.** What small follow-ups became 10x cheaper because of what we just built? An icon that's now one-line because the helper exists; a manual test that became feasible because the dev trigger is wired up; a small UX polish that's a few lines now and a refactor later; a renaming sweep that this feature made obvious. Don't silently postpone them — list them so the user can decide what's worth catching now vs after a break.

The point isn't to slip more work into the current PR — it's to make decisions visible. Often the user will defer all of them and that's fine. But not surfacing them at the natural breakpoint means each one resurfaces later as friction.

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

## Push Back and Surface Ideas

- **Don't hesitate to push back or raise concerns — including about the *interaction design*, not just the code.** If a proposed UX, control scheme, or flow seems confusing, inconsistent, or likely to surprise users, say so plainly and explain why. A dissenting view delivered early is far cheaper than a redesign later, and the user would rather hear it. (Don't manufacture objections — push back when you genuinely see a problem, and be equally willing to say when something is solid.)
- **Proactively offer future UX improvements and feature ideas.** When you notice a worthwhile enhancement adjacent to the work — a smoother interaction, a missing affordance, a feature the new infrastructure now makes cheap — mention it, even if it's out of scope for the current change. Surfacing it lets the user decide; staying silent quietly forecloses the option. Keep ideas concrete and actionable, and don't let them bloat the current change.

