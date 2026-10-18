---
id: variations-preview-toggles
title: Variation Window Preview Toggles
added: 2026-06-11
added-in-version: 4.3
scope: variations-window
tags: [user, essential]
prerequisites:
  - A writable project is open.
  - The Variations window is visible.
  - An operator with existing presets (e.g. [Blob] or [Layer2d]) is available.
related-help:
  - ../.help/docs/using/PresetsAndSnapshots.md
---

The [ui:VariationWindow|Variations window] can give you a quick look at a preset before you commit to
it. This checks the two header toggles — "Preview on hover" and "Live render
previews" — plus the help button, and confirms that the temporary live previews
never replace the default thumbnails that came with a package.

## Step: Header icons are visible

**Action:**
Select an operator with presets (e.g. [Blob]) so the Variations window shows
its presets. Look at the right side of the window header.

**Expected:**
- Three icons sit at the right of the header: a hover-preview toggle, a
  live-preview toggle, and a help icon.
- Toggles that are on are highlighted; ones that are off look dimmed.

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
Note what the current thumbnails look like. Enable the live-preview icon, change
a parameter of the selected operator so the rendering looks clearly different,
and wait until the thumbnails have re-rendered. Then disable the live-preview
icon. Finally, close the project and reopen it.

**Expected:**
- While enabled, the thumbnails re-render live and show the changed output.
- After disabling, the original default thumbnails come back.
- After reopening the project, the presets still show their original default
  thumbnails — the live previews were only temporary and never replaced the
  defaults that ship with the package.

## Step: Explicitly updating defaults still works

**Action:**
Click on empty canvas so nothing is selected, then right-click the canvas and
choose "Update All Thumbnails".

**Expected:**
- The menu entry reads "Update All Thumbnails" while nothing is selected.
- All thumbnails re-render once and are saved as new defaults; they remain after
  toggling live previews on and off.

## Step: Updating only the selected thumbnails

**Action:**
Select two thumbnails (shift-click or drag a fence), then right-click the canvas.

**Expected:**
- The entry now reads "Update Selected Thumbnails".
- Choosing it re-renders only the two selected thumbnails; the others keep their
  previous image.

## Step: Renaming with Enter

**Action:**
Click a single thumbnail so it is selected and the Variations window has focus,
then press `Enter`. Type a new name and press `Enter` again.

**Expected:**
- The first `Enter` opens the rename field at the top of the canvas.
- The second `Enter` closes it and the thumbnail shows the new name.

**Action:**
Click on empty canvas so nothing is selected, then press `Enter`.

**Expected:**
- The rename field opens for the currently active (highlighted) variation.
- With two or more thumbnails selected, `Enter` does nothing.

## Step: Arranging the layout to a grid

**Action:**
Drag a few thumbnails around so the layout has gaps and uneven rows. Right-click
the canvas and choose "Arrange Layout" > "To Grid".

**Expected:**
- The thumbnails pack into a gap-free grid, keeping their row-by-row order.
- The view refits to show all thumbnails.
- `Ctrl+Z` restores the previous positions.

## Step: Arranging the layout by MIDI controller index

**Action:**
Right-click the canvas and choose "Arrange Layout" > "By MIDI Controller Index".

**Expected:**
- Each thumbnail moves to the cell of its index in an 8-column grid, matching
  the controller grid in the Snapshot Control view (index 0 top-left, reading
  order). Unused indices stay as gaps.
- Reopening the project keeps the arranged positions.

## Step: Documentation button

**Action:**
Hover the help icon in the header, then click it.

**Expected:**
- Hovering shows a formatted summary of Presets and Snapshots.
- Clicking opens the wiki page in the browser.
