---
id: build-failure-messages
title: Build-Failure Messages on New Project
added: 2026-06-03
added-in-version: 4.3
scope: project-creation
tags: [dev, edge, essential]
prerequisites:
  - TiXL is closed before the first step. Several steps require modifying PATH
    or moving files before launching the editor.
  - You have administrator access (some steps temporarily rename system files).
  - A scratch project directory is configured under `Settings → Project Directories`
    that is safe to delete after the test.
related-help:
  - ../.help/getting-started/install.md
---

Verifies that known classes of build failure during new-project creation surface
helpful explanations instead of crashing the editor or printing the raw stack to
the user. These paths are catastrophic to get wrong — a brand-new user whose
first action is "create project" sees only this dialog, so the message has to be
intelligible without context.

The dialog under test is opened via `Editor` → menu `File` → `New Project…`,
filling in name + namespace, then clicking `Create`.

## Step: Baseline — happy path creates a project

**Action:**
Launch TiXL normally (PATH unchanged, .NET SDK installed). Open the **New Project**
dialog, fill in a unique project name (e.g. `TestHappyPath`), leave namespace
blank, click `Create`.

**Expected:**
- The dialog closes.
- No error message box appears.
- The new project folder exists under the configured project directory and
  contains a `.csproj`.
- The editor's Console window shows `Project "TestHappyPath" created successfully!`.

## Step: DOTNET_NOT_FOUND — friendly hint when the .NET SDK is missing

**Action:**
Close TiXL. From an admin command prompt, **temporarily** rename the system
`dotnet` shim so it's no longer resolvable on `PATH`:

```
ren "C:\Program Files\dotnet\dotnet.exe" dotnet.exe.disabled
```

(Or remove the `dotnet` directory from your `PATH` env var and start a fresh
TiXL process from a shell that doesn't have it.)

Launch TiXL, open **New Project**, fill in a unique project name, click `Create`.

**Expected:**
- TiXL does **not** crash.
- A dialog titled `Could not create new project` appears.
- The body mentions all three of:
  - That the `dotnet` command was not found in `PATH`.
  - A link/URL to `https://dotnet.microsoft.com/download`.
  - A hint to sign out / reboot if `dotnet` was just installed.
- The dialog has an `Ok` button only (no "Copy error and go to report page" —
  this is the *known cause* branch, not the unknown-failure branch).

**Cleanup:**
After the test, restore the shim:

```
ren "C:\Program Files\dotnet\dotnet.exe.disabled" dotnet.exe
```

(Or restore PATH.)

## Step: NU1100 — friendly hint when a targeting pack is missing

**Action:**
Restart TiXL with the .NET SDK back on PATH. Open **New Project**, fill in a
unique project name (e.g. `TestNu1100`), and **before clicking Create**, edit
the in-flight `.csproj` template on disk under `<repo>/Resources/default-home/`
(or your equivalent path) to set `<TargetFramework>` to a TFM you definitely do
not have installed, such as `net99.0-windows`.

Click `Create`.

**Expected:**
- TiXL does **not** crash.
- A dialog titled `Could not create new project` appears.
- The body mentions all three of:
  - `NuGet could not resolve ... for target framework 'net99.0-windows'`.
  - That the `.NET SDK / targeting pack for 'net99.0-windows'` is not installed.
  - A link/URL to `https://dotnet.microsoft.com/download`.
- The empty `TestNu1100` folder exists on disk — the dialog notes the user
  may want to delete it before retrying.

**Cleanup:**
Restore the `.csproj` template. Delete the half-created `TestNu1100` folder.

## Step: Generic build failure — falls through to bug-report branch

**Action:**
With the template restored, create a fresh project (e.g. `TestGenericFail`).
Once it has been created, open its main `.cs` source file and introduce a
syntax error (e.g. delete a closing brace). In the editor, **save** to trigger
recompilation.

**Expected:**
- TiXL does **not** crash.
- A dialog appears reporting the build failure.
- The dialog includes the button `Copy error and go to report page` (and the
  longer variant with environment variables) — this is the unknown-cause
  branch where the user is offered to file a bug.
- Clicking `Copy error and go to report page` copies the failure log to the
  clipboard and opens the GitHub issues page in the default browser.

**Cleanup:**
Restore the source file. Delete the `TestGenericFail` folder.

## Step: Filesystem failure during project-files creation

**Action:**
Close TiXL. Configure a project directory that is **read-only or non-existent**
— for example, point `Settings → Project Directories[0]` at a path on a removed
USB drive, or at `C:\Windows\ReadOnlyTest\` (which the user normally can't
write to).

Launch TiXL, open **New Project**, fill in `TestReadOnly`, click `Create`.

**Expected:**
- TiXL does **not** crash.
- A dialog titled `Failed to create new project` appears.
- The body mentions `Failed to create project files on disk:` followed by the
  underlying OS error.
- The dialog includes the `Copy error and go to report page` button (generic
  failure branch — filesystem errors don't yet have a known-cause hint).

**Cleanup:**
Restore the project directory.

## Step: Empty ProjectDirectories — defensive fallback

**Action:**
Close TiXL. Edit `userSettings.json` (in `%APPDATA%\TiXL<version>\`) and set
`ProjectDirectories` to an empty array `[]`. Launch TiXL — startup should
auto-populate it with the default folder.

Open **New Project**. Without clicking Create, observe the hint text at the
bottom of the dialog (`Creates a new project. ... You can find your project
in "...".`).

**Expected:**
- TiXL launched successfully.
- The hint text shows a valid path under `Documents\TiXL<version>\` (the
  default fallback), not a missing one and not a crash.
- Clicking `Create` succeeds.

**Cleanup:**
None required — TiXL persisted the default folder on startup.
