---
id: markdown-renderer
title: Markdown Renderer
scope: editor-styling
tags: [smoke, dev]
prerequisites:
  - The TiXL editor is running.
  - The **Utilities** window is open (Window menu, or default layout).
related-help: []
---

Verifies the in-editor Markdown renderer (`MarkdownView`) used by the
manual test runner and operator help. Drives the temporary preview surface
under **Utilities → Markdown preview**. Once the manual test runner adopts
the renderer as a real consumer, this preview category and this test set
should be removed.

## Step: Open the preview

**Context:** TiXL editor is running.
**Action:**
- Open the **Utilities** window.
- Select the **MarkdownPreview** category in the left sidebar.

**Expected:**
- A heading "Markdown preview" appears.
- A multi-line text input shows a sample document.
- Below it, a bordered child renders the sample.

## Step: Headings render correctly

**Context:** Looking at the rendered preview pane.
**Action:**
- Compare the three headings in the preview to the source.

**Expected:**
- `Heading One` is rendered with the **large** font and a muted color.
- `Heading Two` is **bold** at normal size, full text color.
- `Heading Three` is **bold** at normal size, slightly faded.
- Spacing above `# Heading One` is visibly larger than above `## Heading Two`.

## Step: Inline styles render correctly

**Context:** Same view.
**Action:**
- Look at the body paragraphs.

**Expected:**
- `**bold runs**` appears in **bold**.
- `` `inline code` `` appears in a monospace font (JetBrainsMono) and a
  different color from surrounding text.
- Nothing in the rendered output shows literal `*` or `` ` `` characters.

## Step: Links and operator references render

**Context:** Same view.
**Action:**
- Look at the link `TiXL wiki` in the body of `## Heading Two`.
- Look at the operator reference `[RadialGradient]`.

**Expected:**
- Both `TiXL wiki` and `RadialGradient` are colored differently from body text.
- Hovering either changes the mouse cursor to a hand.
- Clicking `TiXL wiki` opens the URL in the system browser.
- Clicking `RadialGradient` writes a log line `[MarkdownPreview] op ref clicked: RadialGradient`
  to the **Console** window. (No navigation — this is just a callback test.)

## Step: Bullet lists with nesting

**Context:** Same view.
**Action:**
- Find the bullet list under `### Heading Three`.

**Expected:**
- Three top-level bullets are visible.
- "nested bullet at depth 1" and "second nested" are indented one step right.
- "depth 2" is indented two steps right.
- "back to depth 0" returns to the leftmost column.
- All bullets use the same `•` glyph regardless of depth.

## Step: Numbered lists with nesting and wrapping

**Context:** Same view.
**Action:**
- Find the numbered list under `## Numbered list`.
- Resize the Utilities window narrower so item 3 wraps.

**Expected:**
- Items 1, 2, 3 render with `1.` `2.` `3.` markers in muted color.
- Nested items render `1.` `2.` indented one step right under item 3.
- When item 3 wraps, the second visual line aligns under the content (not
  under the `3.` marker) — the marker only appears on the first visual line.

## Step: Live edit invalidates the cache

**Context:** Same view, source input on top.
**Action:**
- In the source input, change `# Heading One` to `# Hello World`.

**Expected:**
- The rendered preview updates immediately to show `Hello World`.
- No flicker, lag, or stale layout artifacts.

## Step: Reset to sample

**Context:** Source has been edited.
**Action:**
- Click **Reset to sample**.

**Expected:**
- The source returns to the default sample.
- The preview re-renders the original content.
