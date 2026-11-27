---
id: symbol-library-reference-checks
title: Symbol Library Reference Checks
scope: symbol-library
tags: [projects, sharing]
added: 2026-11-27
added-in-version: 4.4
prerequisites:
  - Two of your own projects exist, here called A and B; B has an image in its Assets folder.
  - Project A is open.
---

Checks the dependency and asset badges in the symbol library, which tell whether an operator would still work
when its project is shared on its own.

## Step: The library scans on its own

**Action:**
Start TiXL and open the Symbol Library before pressing anything in it.

**Expected:**
- The reference columns (usage counts, NOT USED) and the problem-filter (flame) icon are already shown.

## Step: Operators from built-in packages are valid

**Action:**
In project A, create an operator with **Combine**, open it and add a [MidiInput] and a [PlayVideo] inside. Then
press the refresh icon at the top of the Symbol Library and find the new operator under A's namespace.

**Expected:**
- No magenta count appears in the "has invalid references" column for the new operator.

## Step: An operator from another project is flagged

**Action:**
Inside the same operator, add an operator that belongs to project B. Press the refresh icon again.

**Expected:**
- A magenta count appears in the "has invalid references" column.
- Its tooltip lists B's operator under B's namespace.

## Step: An asset of another project is flagged

**Action:**
Add a [LoadImage] inside the operator, pick the image from project B in the Asset Library or the file picker, and
press the refresh icon.

**Expected:**
- A magenta document icon appears next to the operator's reference counts.
- Its tooltip lists the image under "In a package that doesn't ship with TiXL" with the [LoadImage] name.

## Step: Missing files and absolute paths are flagged

**Action:**
In the [LoadImage], type `A:images/does-not-exist.png` (with A's package name) as the path. Add a second
[LoadImage] whose path is an absolute path like `/tmp/test.png` or `C:/temp/test.png`. Press the refresh icon.

**Expected:**
- The tooltip lists the first path under "Not found" and the second under "Absolute path, only valid on this
  machine".

## Step: The asset badge opens a menu

**Action:**
Hover the magenta document icon, then click it. Close the menu and right-click the icon.

**Expected:**
- The icon highlights on hover and its tooltip ends with "Click for options".
- Both clicks open the same menu with **Select Operators in Graph**, **List in Console** and **Select Assets in
  Asset Library**.

## Step: Selecting the affected operators

**Action:**
Navigate to an operator of A that doesn't contain the test operator, then choose **Select Operators in Graph** from
the badge menu.

**Expected:**
- The graph opens the test operator (A uses it, so no new operator is added).
- Both [LoadImage] operators are selected and in view.

## Step: Selecting an operator the project doesn't use

**Action:**
In the Symbol Library, find an operator of project B that has the asset badge and that A doesn't use. With an
editable operator of A open, choose **Select Operators in Graph** from its badge menu.

**Expected:**
- The console says the operator was added to the current operator.
- The graph opens the new instance with the affected operators selected.
- **Undo** removes the added operator again.

## Step: Listing the problems in the console

**Action:**
Back on the test operator in A, choose **List in Console** from its badge menu, open the Console and click one of
the new lines.

**Expected:**
- One warning per problem: operator name, child name, problem and path.
- Clicking a line opens the test operator and selects that [LoadImage].

## Step: Selecting the assets

**Action:**
Type something into the Asset Library search, then choose **Select Assets in Asset Library** from the badge menu.

**Expected:**
- The Asset Library becomes visible and its search is cleared.
- B's image is selected, its folders are open and it is scrolled into view.
- The missing file and the absolute path are not selected (they aren't assets).

## Step: Filtering by asset problems

**Action:**
Open the problem filters (flame icon) in the Symbol Library and enable **Invalid asset references**.

**Expected:**
- The count next to the filter includes your operator.
- The library lists only operators with asset problems, your operator among them.
