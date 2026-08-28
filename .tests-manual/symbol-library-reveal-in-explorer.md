---
id: symbol-library-reveal-in-explorer
title: Symbol Library Reveal in Explorer
added: 2026-08-16
added-in-version: 4.3
scope: symbol-library
tags: [dev, essential]
prerequisites:
  - A project you can edit is open in the Graph Window.
  - The Symbol Library window is visible and its search field is empty.
  - The `Lib` folder in the Symbol Library tree is expanded down to `Lib > image > generate`.
---

The Symbol Library context menus can locate an operator's files on disk, along with the folder of
the project or namespace containing it. Revealing a symbol opens the file browser with that
operator's own file already selected; revealing a project or namespace just opens the folder.

## Step: Reveal a symbol with its file selected

**Action:**
In the Symbol Library tree, right-click the `RadialGradient` operator button under
`Lib > image > generate` and choose `Reveal Symbol in Explorer`.

**Expected:**
- A file browser window opens showing the folder that contains `RadialGradient.cs`,
  `RadialGradient.t3` and `RadialGradient.t3ui`.
- `RadialGradient.cs` is already selected/highlighted in that window — you do not have to search
  the folder listing for it.
- The folder path ends in `Lib\image\generate`.
- The Symbol Library context menu closes and the tree is otherwise unchanged.

## Step: Reveal the project folder of a symbol

**Action:**
Right-click `RadialGradient` again and choose `Reveal Project in Explorer`.

**Expected:**
- A file browser window opens at the root folder of the `Lib` project — the folder holding
  `Lib.csproj`.
- The path is a parent of the folder opened in the previous step.
- Nothing inside the folder is preselected; this entry locates the folder, not a file.

## Step: Reveal a namespace folder

**Action:**
Right-click the `generate` folder row under `Lib > image` in the Symbol Library tree and choose
`Reveal Namespace in Explorer`.

**Expected:**
- The menu entry reads `Reveal Namespace in Explorer` (not `Reveal Project in Explorer`).
- A file browser window opens at the same `Lib\image\generate` folder as the first step.

## Step: Reveal a project folder from its tree row

**Action:**
Collapse the `Lib` folder, then right-click the `Lib` row itself.

**Expected:**
- The menu entry reads `Reveal Project in Explorer`, because `Lib` is a project root rather than a
  sub-namespace.
- Choosing it opens the file browser at the folder holding `Lib.csproj`.

## Step: Reveal a symbol in your own project

**Action:**
In the Symbol Library, expand your own user project folder, right-click any operator you created
there, and choose `Reveal Symbol in Explorer`.

**Expected:**
- A file browser window opens at that operator's folder inside your project, not inside `Lib`.
- The operator's `.cs` file is selected, next to its `.t3` and `.t3ui` files.

## Step: A grouping row offers no folder

**Action:**
Right-click the `user` row in the Symbol Library tree — the row one level above your own project
folder, which groups all user projects together.

**Expected:**
- The `Reveal Namespace in Explorer` entry is greyed out and cannot be clicked.
- `Rename Namespace...` above it stays clickable.
