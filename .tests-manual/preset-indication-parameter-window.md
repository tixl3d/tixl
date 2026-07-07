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
match indication, the emphasized new-preset icon, inline naming, and undo of
preset creation.

## Step: Matching preset is named after applying

**Action:**
Select the operator in the graph. In the Parameter window, open the preset
dropdown next to the instance name and click a preset to apply it.

**Expected:**
- The dropdown shows the applied preset's name in bright text.
- The preset icon right of the dropdown appears dimmed (resting state).

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
- The preset icon right of the dropdown becomes brighter (emphasized), not
  magenta/attention-colored.
- Its tooltip reads "Parameters don't match any preset — save as new preset".

## Step: Returning to stored values restores the match

**Action:**
Undo the parameter change (`Ctrl+Z`) or drag the value back to exactly the
preset's stored value.

**Expected:**
- The dropdown shows the preset name in bright text again.
- The preset icon returns to its dimmed resting state.

## Step: Creating a preset from modified values

**Action:**
Modify a parameter again, then click the emphasized preset icon. Keep the
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
Modify a parameter, click the preset icon again, and press `Escape` instead
of typing a name.

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
- The preset icon is emphasized again (the values match no preset anymore).

## Step: Rename via double-click

**Action:**
Apply a preset, then double-click the dropdown label.

**Expected:**
- The label turns into a focused text field for renaming that preset;
  `Enter` confirms and the new name shows in the dropdown and the
  Variations window.
