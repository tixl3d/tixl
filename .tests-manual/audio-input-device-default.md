---
id: audio-input-device-default
title: Portable Audio Input Device
scope: project-settings
tags: [user, essential, audio]
added: 2026-06-15
added-in-version: 4.2
prerequisites:
  - A project is open.
  - At least one audio input is available on this machine (a microphone, line-in, or a "what you hear" / loopback device).
related-help:
  - ../.help/docs/using/LivePerformances.md
---

A project can react to live audio. This checks that you can leave the input as a
**default** — so the project picks up whatever microphone or line-in each machine
is set to — and still pin it to one specific device when you want to. The default
choice is remembered on your machine, not saved inside the project, so a project
you share keeps working on someone else's setup.

## Step: Switch the project to an external audio device

**Action:**
Open the [ui:ProjectSettings|project play settings] (gear icon in the timeline toolbar). Check
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

## Step: The default isn't baked into the project

**Action:**
Save and close the project. In [ui:Settings] → Audio, change **Default Input Device** to a
different input, then reopen the project.

**Expected:**
- The project's audio now follows the new machine default — it did **not** remember the
  specific device you had picked before. (A shared project behaves the same way on another
  machine: it uses that machine's default, not yours.)

## Step: The same setting drives the global Settings window

**Action:**
Open **Settings → Audio** and scroll to **Default Input Device**.

**Expected:**
- The **Device** dropdown shows the same device chosen in the play settings.
- Changing it here is immediately reflected back in the project play settings' **Default Device** dropdown.

## Step: A specific device can still override per project

**Action:**
Back in the project play settings, open **Input Device** and pick a specific device by name
(anything other than **Default Audio Input**).

**Expected:**
- The **Default Device** picker disappears — the project now sticks to the device you named.
- Save, close, and reopen: the project still uses that exact device, even if the machine's
  default has changed. This is the project overriding the default on purpose.
- Switching **Input Device** back to **Default Audio Input** returns it to following the
  machine default.
