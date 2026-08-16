---
id: symbol-library-reveal
title: Symbol Library Reveal Indicator
added: 2026-08-16
added-in-version: 4.3
scope: symbol-library
tags: [user, essential]
prerequisites:
  - A project you can edit is open in the Graph Window.
  - The composition contains at least one operator, e.g. `[RadialGradient]`.
  - The Symbol Library window is visible and its search field is empty.
---

The Symbol Library highlights the operator the graph is currently pointing at, and shows a
blinking aim icon on any collapsed namespace folder that contains it. Since 4.3 the highlight
falls back to the **current composition** when no operator is selected, so the symbol you are
editing inside can be located too.

## Step: Highlight follows a selected operator

**Action:**
Click `[RadialGradient]` in the Graph Window to select it, then look at the Symbol Library.

**Expected:**
- A blinking outline appears around `RadialGradient` in the library tree, settling into a steady
  outline after roughly half a second.
- If the `Lib` folder containing it is collapsed, a blinking aim icon appears on the right edge
  of that folder row instead.
- Hovering the aim icon shows the tooltip `Reveal selected operator`.

## Step: Highlight falls back to the composition

**Action:**
Double-click an operator in the Graph Window to enter it, then click the empty graph background
so that nothing is selected. Watch the Symbol Library.

**Expected:**
- The highlight moves to the symbol you just entered — the one named in the graph breadcrumb.
- The blink restarts once at the moment the selection clears, then settles.
- No highlight remains on the previously selected operator.

## Step: Reveal the composition from a collapsed folder

**Action:**
With nothing selected inside that composition, click the `Collapse All` icon to the right of the
Symbol Library search field. Hover the aim icon on the collapsed folder row, then click it.

**Expected:**
- The tooltip reads `Reveal current composition`, not `Reveal selected operator`.
- Clicking expands the namespace path down to the composition's symbol and scrolls it into view,
  vertically centered in the tree.
- The revealed symbol carries the blinking highlight.
- **No aim icon is left on any folder row.** Scroll the whole tree top to bottom to confirm — the
  revealed symbol's folders are now open, so nothing should still be pointing at it.

## Step: Reveal lands in the symbol's own namespace

**Action:**
Note the namespace shown for the composition in the graph breadcrumb. Collapse the tree again, click
the aim icon, and check which folder the revealed symbol ended up under.

**Expected:**
- The symbol is revealed inside the folder matching its own namespace, not under a same-named
  operator elsewhere in the tree.

## Step: Multiple selection shows no indicator

**Action:**
In the Graph Window, rubber-band select or `Shift`-click two or more operators.

**Expected:**
- No highlight is shown in the Symbol Library tree.
- No aim icon is shown on any collapsed folder row.
- Clicking the empty graph background restores the composition highlight from the previous step.
