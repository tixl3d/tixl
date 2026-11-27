---
id: asset-library-move
title: Moving Assets and Asset Folders
scope: asset-library
tags: [assets, projects]
added: 2026-11-27
added-in-version: 4.4
prerequisites:
  - Two of your own projects exist, here called A and B.
  - Project A has the folders `images/test/` (with a picture `pic.png`, used by a [LoadImage] in one of A's operators) and `other/` in its Assets.
  - Project A is open and the Asset Library is visible.
---

Moving files and folders in the Asset Library, within a project and into another project, keeps every operator
that uses them working, also after a restart.

## Step: Moving a file to another project

**Action:**
Drag `pic.png` from A's `images/test/` folder onto a folder of project B in the Asset Library. Select the
[LoadImage].

**Expected:**
- The file is listed under B, and no longer under A.
- The [LoadImage]'s path starts with B's package name, without `..` in it, and the image still shows.

## Step: A path assigned after startup follows the move

**Action:**
Without restarting, add a second [LoadImage], point it at the picture with the file picker or by clicking it in the
Asset Library, then drag the picture into another folder of B.

**Expected:**
- Both [LoadImage] operators now point at the new place and still show the image.

## Step: Moving a folder within a project

**Action:**
Move `pic.png` back into A's `images/test/` folder and point the [LoadImage] at it again. Then drag the `test`
folder onto A's `other/` folder.

**Expected:**
- The folder with its contents appears at the new place.
- The [LoadImage]'s path now contains `other/test/pic.png` and the image still shows.
- The console shows no "Can't resolve moved asset" warning.

## Step: Moving a folder to another project

**Action:**
Drag the `test` folder onto a folder of project B.

**Expected:**
- The folder appears in B with `pic.png` inside.
- The [LoadImage]'s path points into B and the image still shows.

## Step: Renaming a folder keeps its files' references

**Action:**
Right-click the `test` folder in B, rename it to `renamed`.

**Expected:**
- The [LoadImage]'s path now contains `renamed/pic.png` and the image still shows.

## Step: The changes survive a restart

**Action:**
Without changing anything else, close TiXL (or wait for the auto-save), start it again and open project A.

**Expected:**
- The [LoadImage] still points at `renamed/pic.png` in project B and shows the image.

## Step: Moves that aren't allowed

**Action:**
Try each of these and watch the console:
- drag a folder onto one of its own subfolders;
- drag a folder onto a folder that already contains a folder with the same name;
- drag a project's top folder (its package name) or a linked folder.

**Expected:**
- The first two log a warning and nothing moves.
- Package folders and linked folders can't be dragged at all.
