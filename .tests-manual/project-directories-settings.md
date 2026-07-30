---
id: project-directories-settings
title: Project Directories Setting
scope: settings
tags: [user, settings]
added: 2026-07-29
added-in-version: 4.2
prerequisites:
  - None.
---

The **Project Directories** list in the Settings window defines which top-level
folders are scanned for projects. This checks that edits are saved immediately,
that a restart button appears after a change, and that the warning tooltip for
missing folders renders correctly.

## Step: Warning tooltip reads normally

**Action:**
Open **Settings → Projects**. Add a directory path that does not exist (e.g.
`C:\DoesNotExist`) to **Project Directories**, then hover the warning icon next
to the entry.

**Expected:**
- A tooltip reads "Folder does not exist" as a normal horizontal line of text —
  not one character per line in a narrow vertical strip.

## Step: Restart button appears after a change

**Action:**
Add or remove an entry in the **Project Directories** list.

**Expected:**
- A **Restart Editor** button appears below the list.
- Hovering it shows a tooltip explaining the restart rescans the project directories.

## Step: Changes persist and restart applies them

**Action:**
Add a real folder containing TiXL projects to the list, then click **Restart Editor**.

**Expected:**
- The editor closes and a new instance starts on its own.
- After the restart, **Settings → Projects** still lists the added folder, and its
  projects show up in the project hub.

## Step: Remove the test entries

**Action:**
Remove the entries added above, then restart via the button again.

**Expected:**
- The list is back to its previous state after the restart.
