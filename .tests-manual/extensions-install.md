---
id: extensions-install
title: Installing and Removing Extensions
added: 2026-11-23
added-in-version: 4.4
scope: settings
tags: [essential, install]
prerequisites:
  - A TiXL release build (in a build from source, extensions compile from the repository and installed copies are skipped).
  - Either a published release with extensions, or a local folder with extensions.json and the .nupkg files from `Installer/pack-extensions.py`, with TIXL_EXTENSIONS_INDEX set to that folder before starting TiXL.
  - For the Ableton Link and MediaPipe steps, Windows.
related-help:
  - ../.help/docs/using/Extensions.md
---

Extensions are optional operator packages (Ableton Link, NDI, MediaPipe) installed from Settings → Extensions. Changes
are staged and applied when TiXL restarts. Run this on Windows and once on Linux or macOS.

## Step: The extensions page

**Action:**
Open **Settings** and select **Extensions**.

**Expected:**
- After a moment, Ableton Link, MediaPipe and NDI are listed, each with a checkbox, a one-line description and its
  licence and size on the right.
- On Linux and macOS, Ableton Link and MediaPipe are greyed out with *Not available on Linux yet* (or macOS).
- **Install & Restart** is disabled while nothing is ticked.

## Step: Installing

**Action:**
Tick **NDI** (and on Windows **Ableton Link**), then click **Install & Restart**.

**Expected:**
- A status line shows the download, then TiXL restarts by itself.
- After the restart the log shows `Installed extension NDI ...` and `Loaded extension Ndi`.
- Back on the extensions page, NDI shows *Installed* and its checkbox is ticked.
- The symbol library contains [NdiInput] and [NdiOutput].

## Step: Examples that use an extension

**Action:**
Open the example [HowToIO] with NDI installed, and once without.

**Expected:**
- With NDI installed, the NDI operators in the example work.
- Without it, they show as missing operators, and no warning about the examples appears in the console.

## Step: Removing

**Action:**
Untick **NDI** and click **Install & Restart**.

**Expected:**
- After the restart the log shows `Removed extension Ndi`, and NDI is unticked without a status line.
- A project of yours that uses [NdiInput] keeps it as a missing operator through a save, and it works again after
  installing NDI again.

## Step: Installing from a file

**Action:**
Click **Install from File...** and pick a `.nupkg` of an extension, then **Restart to Apply**.

**Expected:**
- Before the restart the row shows *Will be installed on restart*.
- Picking a Windows-only extension on Linux or macOS shows *... is built for win-x64, not for linux-x64* instead.

## Step: No extensions published

**Action:**
Start TiXL without network access (and without TIXL_EXTENSIONS_INDEX), then open Settings → Extensions.

**Expected:**
- The page says it could not load the list, offers **Retry**, and **Install from File...** still works.
