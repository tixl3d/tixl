# Editor Markdown Renderer

**Date:** 2026-05-01
**Status:** Outline — not yet scheduled

## Goal

A small, allocation-light Markdown renderer for the TiXL editor that several
features can share:

- **Operator help** (`OperatorHelp.cs`) — currently `TextWrapped` plus a regex
  that highlights `[OpName]` references. Loses bold, lists, code spans, and
  proper link formatting.
- **Manual test runner** (`Plan_ManualTestRunner.md`) — needs bullets, bold,
  inline code, and headings for step bodies parsed from `.tests-manual/*.md`.
- **In-editor release notes / docs** (future) — full `.help/`-style pages
  rendered inside the editor window instead of opened in a browser.

The renderer lives editor-side (`Editor/Gui/Styling/Markdown/`). The Player
does not render help. ImGui font / color dependencies are already there.

## Non-goals

- **Not CommonMark.** No tables, blockquotes, nested lists, HTML, image
  embedding, multiline link references, or footnotes.
- **No custom Markdown extensions** beyond `[OpName]` operator references
  (already convention in operator descriptions).
- **No editing.** Read-only rendering. Editing operator descriptions stays in
  `EditSymbolDescriptionDialog`.
- **No external Markdown library dependency.** TiXL keeps its dependency
  surface small; a hand-rolled parser for this subset is ~200 lines.

## Supported syntax (v1)

| Syntax              | Render                                              |
|---------------------|-----------------------------------------------------|
| `# Heading`         | H1 — see styling table below                        |
| `## Heading`        | H2 — see styling table below                        |
| `### Heading`       | H3 — see styling table below                        |
| `- item` / `* item` | Bullet list, hanging indent on wrap, nestable       |
| `1. item`           | Numbered list, hanging indent on wrap, nestable     |
| `**bold**`          | `Fonts.FontBold`                                    |
| `` `code` ``        | `Fonts.Code` (JetBrainsMono) + tinted color         |
| `[label](url)`      | Link color, click → `OpenWithDefaultApplication`    |
| `[OpName]`          | Operator reference — click navigates / drags        |
| Blank line          | Paragraph break (one line of vertical space)        |

Anything else passes through as plain text. A malformed token (unclosed `**`,
`[label` without `(url)`) is rendered literally rather than swallowed.

### Heading and spacing rules

| Level | Font              | Color                           |
|-------|-------------------|---------------------------------|
| `#`   | `Fonts.FontLarge` | `UiColors.TextMuted`            |
| `##`  | `Fonts.FontBold`  | `UiColors.Text` (default)       |
| `###` | `Fonts.FontBold`  | `UiColors.ForegroundFull.Fade(0.8f)` |

Vertical spacing (via `FormInputs.AddVerticalSpace`, scaled by
`T3Ui.UiScaleFactor` consistently with the rest of `FormInputs`):

- **7** above `#`
- **3** above `##` and `###`
- **2** below any heading before the next text content
- **3** between paragraphs and between list items

"Above" spacing is suppressed whenever the previous rendered line was also a
heading (including at the very start of the document). This keeps stacked
headings tight; the user can still force separation with a blank-line
paragraph break in source.

### Nested lists

Indentation in source determines depth:

```
- top item
  - nested
    - deeper
- back to top
1. numbered
   - bullet inside numbered
   - second bullet
2. numbered continues
```

Rules:

- **One depth level = 2 spaces or 1 tab.** Tabs are normalised to 2 spaces
  during parse. Anything not a multiple of 2 rounds down (lenient).
- **Mixed bullet / numbered nesting is fine** — each depth tracks its own
  kind and (for numbered) its own counter.
- **Numbered counters reset** whenever the previous line at the same depth
  was not a numbered item, or when the parent list ended.
- **Each depth shifts right by `IndentPx`.** Hanging indent on wrap aligns
  with the marker's content column at that depth.
- **Same bullet glyph at every depth** (`•`) for v1. Varying glyphs by depth
  is a polish item, not a v1 feature.

Out of scope for v1 (document, don't implement):

- "Lazy continuation" — paragraph text on a new line aligned under a bullet
  with no marker. Treated as a new paragraph at root depth, which may look
  off; users can work around by repeating the leading whitespace.
- Code blocks or block quotes inside list items.
- Mixed indentation widths within the same document.

## Architecture

```
source string
   ↓ parse  (MarkdownParser)
logical lines + inline runs (slices into source)
   ↓ layout (MarkdownLayout)
LineBox[] with accumulated Y + wrapped fragments
   ↓ render (MarkdownView.Draw)
ImGui calls at the cursor
```

**Why a layout pass:** even at v1 we want consistent bullet hanging-indent and
mixed-style wrapping. Doing both inline with `TextWrapped` is awkward.
Centralising layout also makes Stage 3 culling a drop-in change.

### Public shape

```csharp
public sealed class MarkdownView : IDisposable
{
    public delegate void OperatorRefClicked(string opName);
    public delegate void UrlClicked(string url);

    public MarkdownView(in Options options);

    public void Draw(string markdown,
                     UrlClicked? onUrl = null,
                     OperatorRefClicked? onOperatorRef = null);

    public struct Options
    {
        public float WrapWidthPx;            // 0 = use ContentRegionAvail.X
        public float IndentPx;
        public float ParagraphSpacingPx;
        public float HeadingSpacingPx;
        // Colors pulled from UiColors.* by default; override only for tooltips
    }
}
```

One instance per host window so layout cache lifetime is obvious. Static
caches keyed by hashes are easy to leak across reloads; instance-scoped state
is clearer.

### What the v1 cache looks like

A single layout cache slot per `MarkdownView`. Invalidate when any of these
change:

- source string identity (reference equality is fine for v1; help text and
  test markdown are loaded once and held)
- wrap width
- font scale (`T3Ui.UiScaleFactor`)

That gives near-zero per-frame work for the common case (operator help pinned
open while the user reads it). Multi-entry LRU is Stage 3.

## Stages

Each stage ships a working caller. We don't merge a renderer with no caller.

### Stage 1 — Skeleton + manual test runner

**Effort:** ~2 days

- Implement parser, layout, render for the syntax table above.
- No `ArrayPool`, no culling, no command-stream packing. Plain `List<T>`
  cleared on rebuild.
- Wire into the manual test runner Step view. Replaces the inline rendering
  that `Plan_ManualTestRunner.md` Phase 1 would otherwise need.
- Manual test set: `.tests-manual/markdown-renderer.md` covering each syntax
  bullet (rendered correctly, link click fires callback, hover highlights).

**Deliverable:** `Editor/Gui/Styling/Markdown/MarkdownView.cs` (+ `Parser`,
`Layout`) with the runner using it.

### Stage 2 — Operator help migration

**Effort:** ~1–2 days

- Replace the body of `OperatorHelp.DrawHelp` with a `MarkdownView`. The
  `[OpName]` regex moves into the parser as a first-class run kind.
- Operator-ref click handler resolves the symbol and navigates / starts a
  drag (mirrors current `DocumentationRenderer.DrawReferencedSymbols`
  behavior).
- Side-by-side visual check against the existing help pane on a few operators
  with rich descriptions (e.g. `[RadialGradient]`, `[Camera]`).
- Delete the now-unused `_itemRegex` and ad-hoc rendering paths.

**Deliverable:** Operator help reads bold / lists / code spans from existing
descriptions. No description content needs to change.

### Stage 3 — Long-page support (deferred)

**Trigger:** first caller with a page that scrolls (in-editor `.help/`
viewer or release notes window). Don't do this work speculatively.

- `LineBox.Y` accumulated during layout, binary-search culling on visible
  scroll range.
- `ArrayPool<T>` for parser/layout buffers if profiling shows GC pressure.
- Hybrid wrapping (char-count first, pixel correction on overflow) if
  proportional-font wrapping looks bad in practice.
- Multi-entry LRU cache only if multiple pages render simultaneously.

The Stage 1 architecture is shaped so this is additive, not a rewrite.

## Performance expectations (v1)

For a typical operator description (~500 chars, ~8 lines wrapped):

- Parse + layout: well under 0.1 ms, runs only on text/width change.
- Per-frame draw: dozens of `TextUnformatted` calls, no allocations.
- Steady-state GC: zero bytes/frame after first layout.

Rules from `AGENT_INSTRUCTIONS.md` apply: no LINQ in the draw method, no
closures per fragment, no `string.Substring` — slices are `(start, length)`
indices into the cached source.

## Risks

- **Wrapping across styled fragments.** Mixing `SameLine(0, 0)` with ImGui's
  built-in word wrapping doesn't compose. v1 measures fragments with
  `CalcTextSize` and inserts manual line breaks during layout. That's
  ~30 lines of code but the most subtle part of the implementation.
- **Operator-ref click conflicting with link click.** Both are inline. Make
  sure the parser disambiguates `[OpName]` (no following `(`) from
  `[label](url)` deterministically.
- **Font scale changes.** `T3Ui.UiScaleFactor` shifts mid-session in some
  flows. Layout cache must invalidate on scale change, not just width.
- **Eating layout cost on tooltip flicker.** `OperatorHelp.DrawHelpIcon`
  renders the help inside a tooltip that fades in. Don't rebuild layout on
  every alpha frame — key the cache on (text, width), not on tooltip alpha.

## Testing

- **Manual test set:** `.tests-manual/markdown-renderer.md` — one set ships
  with Stage 1, extended in Stage 2 to cover operator-ref navigation.
- **Visual sanity:** compare operator help before/after Stage 2 on
  `[RadialGradient]`, `[Camera]`, `[BlendInput]` (representative descriptions
  with bold, lists, refs).
- **No automated tests.** The parser is small enough and pure enough that a
  unit test project would be valuable later, but Phase 1 of
  `Plan_AutomaticTests.md` doesn't exist yet — don't gate on it.

## Open questions

- **Spacing above `###`.** The spec doesn't call it out. Default to 3 (same
  as `##`) for Stage 1, revisit if it looks crowded next to nearby `##`
  headings.
- **Inline code background.** Filled rounded rect behind the run, or just a
  color change? Filled background needs draw-list access during render, which
  is fine but slightly more code. Lean: color-only for v1.
- **Should `MarkdownView` own the source string or take it per-call?** Per
  call is simpler; cache invalidation uses reference equality. If we hit
  cases where short-lived strings churn the cache, switch to a hash key.
