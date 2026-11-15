---
id: preset-indication-parameter-window
title: Preset Indication in the Parameter Window
scope: parameter-window
tags: [essential, user]
added: 2026-07-07
added-in-version: 4.3
prerequisites:
  - A project is open with an operator that has presets (e.g. add a [Blob] from Lib, which ships with defaults), or create two presets first.
related-help:
  - ../.help/docs/using/PresetsAndSnapshots.md
---

Covers the preset selector next to the instance name in the Parameter window:
match indication, the emphasized new-preset icon, the per-preset gear menu,
inline naming, and undo of preset creation, writing and removal.

## Step: Matching preset is named after applying

**Action:**
Select the operator in the graph. In the Parameter window, open the preset
dropdown next to the instance name and click a preset to apply it.

**Expected:**
- The dropdown shows the applied preset's name in bright text.
- The new-preset icon right of the dropdown appears dimmed (resting state).

## Step: Match is detected without applying

**Action:**
Restart-free check: select a different operator, then re-select the original
one. (Or save and reopen the project, then select the operator.)

**Expected:**
- The dropdown still names the matching preset — the indication does not
  depend on having clicked it in this session.

## Step: Modifying a parameter emphasizes the new-preset icon

**Action:**
With a preset active, drag any of the operator's parameters to a different
value.

**Expected:**
- The dropdown label dims (still showing the last preset's name).
- The new-preset icon right of the dropdown becomes brighter (emphasized),
  not magenta/attention-colored.
- Its tooltip reads "Parameters don't match any preset — save as new preset".

## Step: Returning to stored values restores the match

**Action:**
Undo the parameter change (`Ctrl+Z`) or drag the value back to exactly the
preset's stored value.

**Expected:**
- The dropdown shows the preset name in bright text again.
- The new-preset icon returns to its dimmed resting state.

## Step: Creating a preset from modified values

**Action:**
Modify a parameter again, then click the emphasized new-preset icon. Keep the
Variations window closed for this step.

**Expected:**
- The dropdown is replaced by a focused text field containing "untitled",
  with the text pre-selected.
- Typing replaces the name; pressing `Enter` confirms it.
- Afterwards the dropdown shows the new name in bright text and the preset
  icon is dimmed — the new preset is active and matches.

## Step: New preset gets a free spot on the Variations canvas

**Action:**
Open the Variations window (Presets mode).

**Expected:**
- The new preset's thumbnail sits on a free spot below or beside the existing
  thumbnails — it does not overlap another preset.

## Step: Escape keeps the preset as "untitled"

**Action:**
Modify a parameter, click the new-preset icon again, and press `Escape`
instead of typing a name.

**Expected:**
- The name field closes; the preset is kept and shows as "Untitled" in the
  dropdown and the Variations window.

## Step: Undo removes the created preset but keeps values

**Action:**
Note the current parameter values, then press `Ctrl+Z`.

**Expected:**
- The just-created preset disappears from the dropdown list and the
  Variations window.
- The operator's parameter values are unchanged.
- The new-preset icon is emphasized again (the values match no preset anymore).

## Step: Rename via double-click

**Action:**
Apply a preset, then double-click the dropdown label.

**Expected:**
- The label turns into a focused text field for renaming that preset;
  `Enter` confirms and the new name shows in the dropdown and the
  Variations window.

## Step: Opening the dropdown shows no phantom hover

**Action:**
Modify a parameter so no preset matches, move the mouse away from the
dropdown's list area, then click the dropdown to open it.

**Expected:**
- No row is highlighted until the mouse moves over a row or the arrow keys
  are pressed.
- With "Preview on hover" enabled, the output does not change on opening.

## Step: Preset gear menu

**Action:**
Apply a preset, open the dropdown and click the gear icon on that preset's
row.

**Expected:**
- Clicking the gear does not apply the preset or close the dropdown.
- The menu starts with a small "Preset" header, followed by Write Current
  Values, Rename, Update Thumbnail, a separator and Remove.
- Write Current Values is disabled because the values match the preset.

## Step: Write current values into a preset

**Action:**
Change a parameter (or apply nothing and edit values so the dropdown shows a
preset count). Open the dropdown, click the gear on any preset and choose
**Write Current Values**.

**Expected:**
- The dropdown names that preset in bright text — it now matches.
- The preset's thumbnail updates in the dropdown and the Variations window.
- `Ctrl+Z` restores the preset's previous values: the dropdown no longer
  names it.

## Step: Rename and Update Thumbnail from the gear menu

**Action:**
From a preset's gear menu choose **Rename**, type a new name and press
`Enter`. Then change the output view (e.g. pin a different downstream
operator) and choose **Update Thumbnail** from the gear menu.

**Expected:**
- Rename closes the dropdown and turns it into a focused name field for that
  preset, like double-clicking.
- Update Thumbnail re-renders the preset's thumbnail.

## Step: Remove a preset

**Action:**
Open the dropdown, click the gear on a preset and choose **Remove**.

**Expected:**
- The preset disappears from the dropdown list and the Variations window.
- `Ctrl+Z` brings it back.
