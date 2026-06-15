---
id: audio-input-device-default
title: Portable Audio Input Device
scope: project-settings
tags: [essential, audio]
added: 2026-06-15
added-in-version: 4.2
prerequisites:
  - A project is open.
  - At least one WASAPI audio input (microphone, line-in, or loopback device) is available on this machine.
related-help:
  - ../.help/docs/using/LivePerformances.md
---

Verifies that the external audio input device a project uses can be left as a
machine-independent default — so a shared project resolves to whatever device
each machine is configured for — while still allowing a per-project override.
The machine's default device is stored on the machine (in `projectSettings.json`),
not inside the project file.

## Step: Switch the project to an external audio device

**Action:**
Open the project play settings (gear icon in the timeline toolbar). Check
"Specify settings for <project>" if needed, then set **Audio Source** to
**External Device**.

**Expected:**
- The external-device controls appear (Sync Mode, BPM, gain, input level meter, Input Device).
- The **Input Device** dropdown reads **Default Audio Input**.

## Step: The machine-local device picker appears under the default

**Action:**
With **Input Device** still on **Default Audio Input**, look directly below the dropdown.

**Expected:**
- A second dropdown labelled **Default Device** is shown.
- A hint explains it is stored per machine and keeps shared projects portable.

## Step: Choose the machine's default input

**Action:**
Open the **Default Device** dropdown and select a real input device that carries signal
(speak into the mic / play audio for a loopback device).

**Expected:**
- The Input Level meter responds to the incoming signal.
- The selection persists if you close and reopen the play settings.

## Step: The default selection is not written into the project

**Action:**
Save the project. Open its `.t3` file in a text editor and find `"AudioInputDeviceName"`.

**Expected:**
- `"AudioInputDeviceName"` is an empty string (`""`) — the concrete device name is **not** stored in the project.

## Step: The same setting drives the global Settings window

**Action:**
Open **Settings → Audio** and scroll to **Default Input Device**.

**Expected:**
- The **Device** dropdown shows the same device chosen in the play settings.
- Changing it here is immediately reflected back in the project play settings' **Default Device** dropdown.

## Step: A specific device can still override per project

**Action:**
Back in the project play settings, open **Input Device** and select a concrete device by name
(not **Default Audio Input**).

**Expected:**
- The **Default Device** picker disappears (the project now uses an explicit override).
- Saving and reopening the `.t3` shows that device name stored in `"AudioInputDeviceName"`.
- Selecting **Default Audio Input** again clears it back to the machine default.
