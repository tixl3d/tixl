---
id: combine-project-dropdown
title: Combine / Duplicate dialogs — project dropdown defaults and read-only group
added: 2026-08-09
added-in-version: 4.3
scope: graph
prerequisites:
  - At least two user projects loaded, plus a debug build (built-in packages such as Lib and Examples are only listed as projects there).
---

The **Project** dropdown of the Combine and Duplicate dialogs now sorts case-insensitively, defaults
to the project the selection already lives in, and pushes the built-in TiXL packages into a disabled
"Read Only" section at the bottom of the list.

## Step: Default is the current project

**Action:**
Open project **A**, select two ops inside it, right-click → **Combine into New Type...**.

**Expected:**
- The Project field shows **A**, and Namespace starts with A's root namespace.

**Action:**
Cancel. Switch the dropdown-affecting state: repeat the combine, but this time pick project **B** in
the dropdown and press **Cancel** again. Now open project **A** again, select ops, and open the
Combine dialog.

**Expected:**
- The Project field shows **A** again — the previously picked **B** is *not* remembered.

## Step: Read-only group

**Action:**
Open the Project dropdown.

**Expected:**
- Writable user projects are listed first, in alphabetical order ignoring case (`_Tests`, `Archive`,
  `examples`… sorted as if all lowercase — `Playground` sorts before `skills`, `skills` before
  `Tixl42_Release`).
- Below them a small gray **Read Only** label, followed by the built-in packages (`Lib`, `Types`,
  `Video`, `Io`, `ndi`, `spout`, …) drawn in muted gray.
- Clicking a muted entry does nothing — the dropdown stays open and the selection is unchanged.

## Step: Already inside a built-in package (debug builds)

**Action:**
Navigate into a `Lib` operator, select two child ops, and open the Combine dialog.

**Expected:**
- The Project field shows **Lib** and the Combine button is available — being *already* in a built-in
  package still lets you combine there.
- In the dropdown, `Lib` is the one built-in entry that is *not* grayed out; the others still are.

## Step: Duplicate dialog behaves the same

**Action:**
Select a single op in a user project and choose **Duplicate as New Type...**.

**Expected:**
- Project defaults to the op's own project; the dropdown shows the same sorted list and Read Only
  group.
