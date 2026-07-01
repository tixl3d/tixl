---
id: markdown-renderer
title: Markdown Renderer
added: 2026-05-01
added-in-version: 4.2
scope: editor-styling
tags: [dev, smoke]
prerequisites:
  - The TiXL editor is running.
  - The Utilities window is open (Window menu, or default layout).
related-help: []
---

Verifies the in-editor Markdown renderer (`MarkdownView`) used by the manual test
runner and operator help. Drives the temporary preview surface under
**Utilities → Markdown preview**. Once the runner adopts the renderer as a real
consumer, this preview category and this test set should be removed.

## Step: Opening the preview

**Action:**
Open the **Utilities** window and select the **MarkdownPreview** category in the
left sidebar.

**Expected:**
- A heading "Markdown preview" appears.
- A multi-line text input shows a sample document.
- Below it, a bordered child renders the sample.

## Step: Verifying the headings

**Action:**
Look at the rendered preview pane and compare the three headings to the source.

**Expected:**
- `Heading One` renders with the **large** font and a muted color.
- `Heading Two` is **bold** at normal size, full text color.
- `Heading Three` is **bold** at normal size, slightly faded.
- The spacing above `# Heading One` is visibly larger than above
  `## Heading Two`.

## Step: Verifying inline styles

**Action:**
Look at the body paragraphs in the rendered output.

**Expected:**
- `**bold runs**` appears in **bold**.
- `` `inline code` `` appears in a monospace font (JetBrainsMono) and a
  different color from surrounding text.
- Nothing in the rendered output shows literal `*` or `` ` `` characters.

## Step: Clicking a link

**Action:**
Find the link `TiXL wiki` in the body of `## Heading Two` and click it.

**Expected:**
- The link is colored differently from body text.
- Hovering changes the mouse cursor to a hand.
- Clicking opens the URL in the system browser.

## Step: Clicking an operator reference

**Action:**
Find the operator reference `[RadialGradient]` in the body of `## Heading Two`
and click it.

**Expected:**
- The reference is colored differently from body text.
- Hovering changes the mouse cursor to a hand.
- Clicking writes a log line `[MarkdownPreview] op ref clicked: RadialGradient`
  to the Console window. (No navigation — this is just a callback test.)

## Step: Bullet lists with nesting

**Action:**
Find the bullet list under `### Heading Three` and check that the indentation
mirrors the source structure.

**Expected:**
- Three top-level bullets are visible.
- "nested bullet at depth 1" and "second nested" are indented one step right.
- "depth 2" is indented two steps right.
- "back to depth 0" returns to the leftmost column.
- All bullets use the same `•` glyph regardless of depth.

## Step: Numbered lists with wrapping

**Action:**
Find the numbered list under `## Numbered list`, then resize the Utilities
window narrower so item 3 wraps to a second visual line.

**Expected:**
- Items 1, 2, 3 render with `1.` `2.` `3.` markers in a muted color.
- Nested items render `1.` `2.` indented one step right under item 3.
- When item 3 wraps, the second visual line aligns under the content (not
  under the `3.` marker) — the marker only appears on the first visual line.

## Step: Live editing invalidates the cache

**Action:**
In the source input at the top of the preview, change `# Heading One` to
`# Hello World`.

**Expected:**
- The rendered preview updates immediately to show `Hello World`.
- No flicker, lag, or stale layout artifacts.

## Step: Resetting to the sample

**Action:**
After editing the source, click **Reset to sample**.

**Expected:**
- The source returns to the default sample.
- The preview re-renders the original content.
