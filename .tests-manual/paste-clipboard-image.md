---
id: paste-clipboard-image
title: Pasting Clipboard Images into the Graph
scope: graph
tags: [user]
added: 2026-11-26
added-in-version: 4.4
prerequisites:
  - An editable project is open with a graph window visible.
  - A screenshot tool or an image viewer that can copy an image to the clipboard.
---

Pasting while the clipboard holds an image should save it as a PNG into the project's
`Assets/images/pasted/` folder and create a LoadImage operator that shows it.

## Step: Paste a screenshot

**Action:**
Take a screenshot to the clipboard (Windows: <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd>; macOS: <kbd>Cmd</kbd>+<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>4</kbd>; Linux: the desktop's screenshot tool with "copy to clipboard"). Hover the graph and press <kbd>Ctrl</kbd>+<kbd>V</kbd>.

**Expected:**
- A LoadImage operator appears at the mouse position, selected, and its output shows the screenshot.
- A toast reads "Pasted image saved as ..." with the asset address.
- The Assets window lists `images/pasted/pasted_<date>_<time>.png`; the file on disk is a valid PNG.

## Step: Paste twice within a second

**Action:**
Press <kbd>Ctrl</kbd>+<kbd>V</kbd> twice quickly.

**Expected:**
- Two operators and two files; the second file carries a `_2` suffix instead of overwriting the first.

## Step: Paste from the menus

**Action:**
Right-click the graph background and choose **Paste**; then use **Edit → Paste**.

**Expected:**
- Each creates another LoadImage operator with a new pasted image (context menu at the mouse, Edit menu at the view center).

## Step: Undo

**Action:**
Press <kbd>Ctrl</kbd>+<kbd>Z</kbd>.

**Expected:**
- The last LoadImage operator is removed. The PNG stays in `Assets/images/pasted/`, as with dropped files.

## Step: Operator copy and paste still works

**Action:**
Select two operators, press <kbd>Ctrl</kbd>+<kbd>C</kbd>, then <kbd>Ctrl</kbd>+<kbd>V</kbd>. Also try **Duplicate** (<kbd>Ctrl</kbd>+<kbd>D</kbd>).

**Expected:**
- The operators are pasted as before; no image is created.
