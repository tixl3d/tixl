---
id: variations-preview-toggles
title: Variation Window Preview Toggles
added: 2026-06-11
added-in-version: 4.3
scope: variations-window
tags: [essential]
prerequisites:
  - A writable project is open.
  - The Variations window is visible.
  - An operator with existing presets (e.g. [Blob] or [Layer2d]) is available.
related-help:
  - ../.help/docs/using/PresetsAndSnapshots.md
---

Covers the header icon toggles for "Preview on hover" and "Live render
previews" in the Variations window, the documentation button, and that live
previews never overwrite the default thumbnails shipped with a package.

## Step: Header icons are visible

**Action:**
Select an operator with presets (e.g. [Blob]) so the Variations window shows
its presets. Look at the right side of the window header.

**Expected:**
- Three icons are right-aligned in the header: a hover-preview toggle, a
  live-preview toggle, and a help icon.
- Active toggles are highlighted in the attention color; inactive ones are
  dimmed.

## Step: Toggles stay in sync with the context menu

**Action:**
Right-click the canvas background and toggle "Preview on Hover" and
"Live Render Previews" from the context menu. Then check the header icons.

**Expected:**
- The header icons reflect the state set via the context menu, and vice versa.

## Step: Hover preview toggle

**Action:**
Enable the hover-preview icon and hover over a preset thumbnail. Then disable
it and hover again.

**Expected:**
- Enabled: hovering temporarily applies the preset to the output; moving away
  restores the previous state.
- Disabled: hovering has no effect on the output.

## Step: Live previews don't replace default thumbnails

**Action:**
Note the current thumbnails. Enable the live-preview icon, change a parameter
of the selected operator so the rendering looks clearly different, and wait
until the thumbnails have re-rendered. Then disable the live-preview icon.

**Expected:**
- While enabled, thumbnails re-render live and show the changed output.
- After disabling, the original default thumbnails reappear.
- The files under the package's `.meta/Thumbnails/` folder are unchanged
  (live previews are written to the user's temp folder instead).

## Step: Explicitly updating defaults still works

**Action:**
Right-click the canvas and choose "Update thumbnails".

**Expected:**
- Thumbnails re-render once and are saved as new defaults; they remain after
  toggling live previews on and off.

## Step: Documentation button

**Action:**
Hover the help icon in the header, then click it.

**Expected:**
- Hovering shows a formatted summary of Presets and Snapshots.
- Clicking opens the wiki page in the browser.
