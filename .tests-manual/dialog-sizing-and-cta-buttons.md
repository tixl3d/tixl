---
id: dialog-sizing-and-cta-buttons
title: Dialog sizing and primary action buttons
scope: dialogs
tags: [essential, user]
added: 2026-07-03
added-in-version: 4.2
prerequisites:
  - A writable (non-library) project is open with at least one operator in the graph.
---

Verifies that modal dialogs grow with their content instead of cutting off the
primary button at the bottom, and that the primary action uses the large
call-to-action button style.

## Step: New Project dialog shows its Create button

**Action:**
Open the New Project dialog (e.g. from the hub or the File menu).

**Expected:**
- The dialog is tall enough that the `Create` button and the hint text below it are fully visible without scrolling.
- `Create` is drawn as a large call-to-action button; `Cancel` remains a normal small button.
- While the name field is invalid (e.g. cleared), the `Create` button appears strongly faded and clicking it does nothing.

## Step: Dialogs grow with warnings

**Action:**
Right-click an operator from the `Lib` package and choose `Rename Input` (or open any dialog that shows extra warning hints, e.g. Duplicate as new Symbol with a long description).

**Expected:**
- All content including the primary button at the bottom is visible; nothing is cut off.
- If the content would exceed the screen height, a vertical scrollbar appears instead of hiding the buttons.

## Step: Duplicate and Combine dialogs use CTA buttons

**Action:**
Select an operator and open `Duplicate as new Symbol`; afterwards open `Combine into new Symbol` with one or more operators selected.

**Expected:**
- `Duplicate` / `Combine` are drawn as large call-to-action buttons and are fully visible at the bottom of the dialog.
- With an invalid or duplicate symbol name, the button is faded and unclickable.
