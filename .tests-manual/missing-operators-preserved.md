---
id: missing-operators-preserved
title: Missing Operators Are Preserved
scope: project-loading
tags: [data-safety]
added: 2026-09-17
added-in-version: 4.3
prerequisites:
  - A user project under version control (or a copy), so file changes can be diffed.
  - The editor is closed at the start.
---

Operators whose symbol can't be found on load (missing package, deleted operator) are hidden in the
graph but stay in the `.t3` / `.t3ui` files when saving. This set simulates a missing symbol by
editing a `SymbolId` by hand.

The operator name in the warning comes from the `SymbolName` attribute, which is written on save. Save
the symbol once in the editor before starting, so the attribute exists.

## Step: Simulating a missing operator

**Action:**
With the editor closed, open the `.t3` file of a symbol in your project that has several connected
and at least one animated operator. Pick an animated child that has connections and change the last
digit of its `SymbolId`. Save the file and start the editor.

**Expected:**
- The editor starts normally.
- The console shows one warning summarising the unavailable operators, listing the project, the
  affected symbol, and the operator by **namespace and name** (not only a Guid).
- There is no "Not saving [...]" message.

## Step: Reading the startup summary

**Action:**
Right after startup (once other startup dialogs are closed), look at the "Warning: Missing Symbols"
popup. Hover and click the help icon, click the folder icon of the affected symbol, then `Show in Graph`.

**Expected:**
- The popup states how many operators in how many symbols could not be found, grouped by project,
  naming the affected symbol and the missing operator.
- The help icon opens the Pitfalls page of the documentation.
- The folder icon has a "Reveal in Explorer" tooltip and shows the symbol's `.t3` file in the file browser.
- `Show in Graph` closes the popup, opens the symbol in the graph and frames the missing operator.
- The popup does not appear again until the next start.

## Step: Seeing the gap in the graph

**Action:**
With the affected symbol open, look at the position where the edited operator used to be. Then pan away until it leaves the view.

**Expected:**
- A magenta outlined box with the operator's name and "missing" sits at the operator's position.
- Magenta wires connect it to the operators it was connected to; operators it fed still show the
  affected input line, so their height and any snapped stacks are unchanged.
- Hovering the box shows the full namespace and name and the symbol id.
- The box can't be selected or dragged.
- Once it is outside the view, a magenta marker on the graph window's border points towards it.

## Step: Editing and saving the affected symbol

**Action:**
In the affected symbol, move another operator,
change one of its parameters, and save with `Ctrl+S`.

**Expected:**
- Saving succeeds without warnings about the affected symbol.
- In a diff of the `.t3` file, the child with the edited `SymbolId` is still present with its input
  values and its `SymbolName`, its connections are still listed, and its `Animator` entries
  are still there. Only your deliberate edit shows up.
- In a diff of the `.t3ui` file, the entry for that child id still exists with its position.

## Step: Deleting and undoing

**Action:**
Right-click the magenta box and choose `Delete Missing Operator`. Then press `Ctrl+Z`, and `Ctrl+Shift+Z`
(redo), and `Ctrl+Z` again so the box is back.

**Expected:**
- The context menu shows a group named after the operator with "(Missing)".
- Deleting removes the box and its wires; the inputs it fed return to their unconnected look.
- Undo brings the box and all wires back, redo removes them again.
- After the final undo and a save, the `.t3` file still contains the child, its connections and its
  `Animator` entries.

## Step: Restoring the operator

**Action:**
Close the editor, revert the `SymbolId` edit in the `.t3` file (keep the other saved changes), and
start the editor again.

**Expected:**
- The operator is back at its original position with its parameters, connections and animation.
- Multi-input connections that included the operator are in their original order.
- No warning about unavailable operators is logged.
