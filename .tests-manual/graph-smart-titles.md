---
id: graph-smart-titles
title: Smart Titles
scope: graph-window
tags: [user]
added: 2026-11-27
added-in-version: 4.4
prerequisites:
  - A project you can edit is open in the Graph Window.
  - The graph contains one [Multiply], one [Text] and one operator of your own project (any symbol you can edit with at least one float parameter).
related-help:
  - ../.help/docs/getting-started/Concepts.md
---

Checks that operators show their smart title in the graph, that it follows parameter changes, falls back when it
can't be shown, and that editing it works with undo and survives saving.

## Step: Lib operators show their smart title

**Action:**
Select the [Multiply] and set its `B` parameter to `0.3`. Select the [Text] and set its text to `TiXL loves you`.

**Expected:**
- The [Multiply] node shows `Multiply` with `* 0.3` in smaller text below it.
- The [Text] node shows `Text` with `TiXL loves you` below it.
- Typing a line break after `TiXL` shows `TiXL loves you` on one line, with a space in place of the break.
- Typing a long text cuts the second line at the node's right edge with `…`.
- Changing `B` to `2.5` changes the second line to `* 2.5` immediately.

## Step: Zooming

**Action:**
Zoom out of the graph slowly, then zoom far in.

**Expected:**
- Zoomed out, the second line fades away before it gets too small to read, and the name moves back to the middle of the node.
- Zoomed far in, both lines grow and the second line stays smaller than the name.

## Step: Operators that load a file

**Action:**
Add a [LoadImage] and pick an image file.

**Expected:**
- The node shows `LoadImage` with the file name below it in smaller text, and the image thumbnail next to it.
- Picking another file updates the second line.

## Step: Fallback for connected parameters

**Action:**
Connect a [Value] to the `B` input of the [Multiply].

**Expected:**
- The node shows only `Multiply`, centered.
- After disconnecting, `* 2.5` appears below the name again.

## Step: Custom names win

**Action:**
Rename the [Multiply] to `Gain` in the Parameter Window. Then clear the name again.

**Expected:**
- While renamed, the node shows only `"Gain"` in quotes.
- With the name cleared, it shows `Multiply` with `* 2.5` below again.

## Step: Use as Title

**Action:**
Select the operator of your own project. Right-click one of its float parameters in the Parameter Window and choose
**Use as Title**. Open the same context menu again.

**Expected:**
- The node shows the parameter's value below its name, e.g. `0.5`.
- **Use as Title** now has a checkmark.
- Choosing it again removes the second line and clears the checkmark.
- `Ctrl+Z` and `Ctrl+Shift+Z` toggle the title back and forth.

## Step: Editing the template

**Action:**
Open **Edit description** for the same operator. In **Smart Title**, type `Amount ` and pick the parameter from
**Insert parameter**. Then type `{Nope}` at the end.

**Expected:**
- After inserting, the preview line shows `Preview with default values: Amount ...` and the node updates while typing.
- With `{Nope}` the line turns into a warning `Unknown parameter "Nope"` and the node's second line shows `{Nope}` as text.
- Removing `{Nope}` and closing the dialog keeps the title. One `Ctrl+Z` restores the previous title.

## Step: Number formats

**Action:**
Change the template to `{<parameter name>:0.00}` and set the parameter to `1.23456`.

**Expected:**
- The node's second line shows `1.23`.

## Step: Survives saving, reloading and duplicating

**Action:**
Save the project, restart TiXL and open the project again. Then right-click the operator and choose **Duplicate as New Type...**.

**Expected:**
- The operator still shows its smart title after the restart.
- The new instance of the duplicated symbol shows the same smart title with its own value.
