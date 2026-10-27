---
id: parameter-popover
title: Parameter Popover
scope: graph-window
tags: [user, essential]
added: 2026-10-28
added-in-version: 4.4
prerequisites:
  - A project you can edit is open in the Graph Window.
  - A [LinePoints] operator (or another one with numbers, vectors, a color and a dropdown) is selected.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Checks editing parameters with the keyboard in the popover that `S` opens. Every
change previews while you type and is undone in one step with `Ctrl+Z`.

## Step: Opening the popover

**Action:**
With the operator selected and the Graph Window focused, press `S`.

**Expected:**
- A popover appears at the bottom of the graph, listing the operator's parameters with their values.
- The first parameter that isn't connected has the focus, shown by a darker row.
- The help line at the bottom shows the keys for the focused parameter.

## Step: Filtering keeps the order

**Action:**
Type `c`, then `o`.

**Expected:**
- Parameters that don't match fade out; the remaining ones slide together and keep their order.
- The matching letters are underlined, and the best match (e.g. Count) has the focus.
- The top of the popover stays in place while the list shrinks.

## Step: Typing and applying a value

**Action:**
With Count focused, press `Tab`, type `100`, then press `Down`.

**Expected:**
- While typing, the operator's output already shows 100 lines.
- After `Down` the value flashes briefly and the next parameter is focused.
- `Ctrl+Z` after closing the popover restores the previous count.

## Step: Calculating from the current value

**Action:**
Move back to Count with `Up`, press `Tab` and type `*2`.

**Expected:**
- The value becomes `100*2` and a faint `= 200` appears after it.
- Typing `-5` instead (after `Esc` and `Tab`) sets -5 rather than subtracting.

## Step: Vectors and colors

**Action:**
Focus a vector parameter, press `Tab` and type `0 1 0`. Then focus a color parameter, press `Tab` and type `ora`.

**Expected:**
- The vector previews as 0 1 0.
- The color previews as orange, with `nge` completed faintly after the typed text.

## Step: Dropdown options

**Action:**
Focus a dropdown parameter, press `Tab` and type the first letters of an option, then press `Down` and `Up`.

**Expected:**
- The best matching option is previewed and completed faintly.
- `Down` and `Up` switch between the matching options without leaving the parameter.

## Step: Adjusting with the slider

**Action:**
Focus a number, tap `Right` once, then hold it. Release, hold `Shift` and press `Right` again. Press `Esc`.

**Expected:**
- A tap moves the value by one step; holding accelerates and stops on release.
- The infinity slider shows above the popover; with `Shift` the steps are finer.
- `Esc` restores the value from before adjusting, and the popover stays open.

## Step: Color brightness and hue

**Action:**
Focus a color, press `Right` a few times. Then type `h` and press `Right`.

**Expected:**
- Without typing, Right makes the color brighter.
- After `h`, Right shifts the hue; the row shows `hue` and its value.

## Step: Reset and insert

**Action:**
Change a value and apply it, then press `Shift+R` on it. Then focus a texture or number input and press `Shift+I`.

**Expected:**
- `Shift+R` resets the parameter to its default and keeps the focus on it.
- `Shift+I` closes the popover and opens the symbol browser connected to that input, with the first matching operator selected.

## Step: Esc steps back

**Action:**
Type a filter, press `Tab`, type a value, then press `Esc` three times.

**Expected:**
- The first `Esc` clears the value, the second clears the filter, the third closes the popover.
- After closing, keyboard shortcuts work in the graph again without clicking it.

## Step: Remembering the last parameter

**Action:**
Apply a value to the third parameter, close the popover, and press `S` again.

**Expected:**
- The popover opens with the focus on the parameter you edited last.
