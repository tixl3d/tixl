---
id: sync-blocked-project-dialog
title: Sync-Blocked Project Dialog
scope: startup
tags: [edge, dev]
added: 2026-08-03
added-in-version: 4.2
prerequisites:
  - A disposable test project exists in the default projects folder inside Documents (e.g. `%USERPROFILE%\Documents\TiXL4.2\TestProject`).
  - TiXL is closed.
related-help:
  - ../.help/docs/install/Installation.md
---

Verifies the "Could not load Project" dialog that appears after startup when a project could not
be loaded because of an access-denied error inside a Windows-managed folder (the narrow indicator
for OneDrive/Dropbox interference). The test simulates the sync-tool lock with a deny ACL on the
project file.

## Step: Simulating a sync-blocked project

**Action:**
In a terminal, deny yourself read access to the test project's `.csproj` file:
`icacls "%USERPROFILE%\Documents\TiXL4.2\TestProject\TestProject.csproj" /deny "%USERNAME%:R"`
Then start TiXL and wait for startup to complete (close the welcome window if it opens).

**Expected:**
- A modal dialog titled **Could not load Project** appears.
- It states that TiXL was not allowed to load the listed projects, showing the test project's path with its folder name emphasized.
- The text mentions sync tools like OneDrive or Dropbox as a frequent cause.
- The Projects panel lists the project under **Broken**.

## Step: Inspecting the suggested fix

**Action:**
In the dialog's *Suggested Fix* section, leave the checkbox unchecked, then check
"Try to move these project folders to...". Open the dropdown next to it.

**Expected:**
- While unchecked, the dialog only offers a **Close** button.
- Once checked, **Move and Restart** and **Cancel** buttons appear instead, along with a
  "Create backup before moving" checkbox that is on by default.
- The dropdown lists a `TiXL` folder on each local drive (e.g. `C:/TiXL/`) plus a **Custom** entry.
- Selecting **Custom** reveals a *Folder* text field with a `...` browse button.

## Step: Detecting a target conflict

**Action:**
In Explorer, create the folder the move would produce (e.g. `C:\TiXL\TestProject`) and place any
file inside it. Back in the dialog, switch the dropdown to another entry and back to `C:/TiXL/`
so it revalidates.

**Expected:**
- A warning states the folder already exists and is not empty, listing its path.
- The **Move and Restart** button is disabled.
- After deleting the conflicting folder and reselecting the target, the warning disappears and the button is enabled again.

## Step: Moving the projects and restarting

**Action:**
Before moving, restore your file access in the terminal:
`icacls "%USERPROFILE%\Documents\TiXL4.2\TestProject\TestProject.csproj" /remove:d "%USERNAME%"`
Then, with the dialog still open and a drive target like `C:/TiXL/` selected, click **Move and Restart**.

**Expected:**
- TiXL restarts automatically.
- After the restart, the test project loads normally and appears in the project list.
- The project folder now exists under the chosen target (e.g. `C:\TiXL\TestProject`) and is gone from Documents.
- The moved folder contains a pinned backup zip tagged `preMove...` under `.temp\Backup`.
- *Settings → Projects → Project Directories* contains the new target folder.

## Step: Cleaning up

**Action:**
Delete the moved test project folder and remove the added entry from
*Settings → Projects → Project Directories*.

**Expected:**
- After a restart, TiXL starts without the dialog and without the test project.
