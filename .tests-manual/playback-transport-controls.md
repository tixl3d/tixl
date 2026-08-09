---
id: playback-transport-controls
title: Transport Controls Follow Project Setup
scope: project-settings
tags: [user, essential, playback]
added: 2026-08-09
added-in-version: 4.3
prerequisites:
  - A project is open and the timeline is visible (Menu → View → Timeline).
related-help:
  - ../.help/docs/using/LivePerformances.md
---

The timeline toolbar shows either the **transport controls** (jump to start, previous
keyframe, play backwards, play forward, next keyframe, loop, Dope/Curve) or the
**beat-tap controls** (a BPM value field and a **Sync** button). Which set you get must
follow **Project Setup** in the composition settings. This checks that a round trip through
Live/Interactive never leaves an Animation project stuck with the beat-tap controls.

## Step: Animation mode shows the transport controls

**Action:**
Open the [ui:ProjectSettings|composition settings] (gear icon in the timeline toolbar).
Check "Specify settings for &lt;project&gt;" if it isn't checked. On the **Playback** page set
**Project Setup** to **Animation**. Close the settings window and look at the timeline toolbar.

**Expected:**
- The toolbar shows seven controls between the beat-grid square and the speaker icon:
  jump-to-start, previous-keyframe, play-backwards, play-forward, next-keyframe, loop,
  and a button reading **Dope** or **Curve**.
- There is no BPM field and no **Sync** button in the toolbar.
- The record button (rightmost) is enabled — hovering it offers to start a recording rather
  than explaining that recording is unavailable.

## Step: Live/Interactive with tapping shows the beat-tap controls

**Action:**
Reopen the composition settings, set **Project Setup** to **Live Interactive**, then set
**Sync Mode** to **Tapping**. Close the settings window.

**Expected:**
- The seven transport controls are replaced by a BPM value field and a **Sync** button.
- The record button is dimmed. Hovering it says recording needs a timeline playhead and
  names the two settings that restore it.

## Step: Switching back to Animation restores the transport controls

**Action:**
Reopen the composition settings and set **Project Setup** back to **Animation**.
Close the settings window.

**Expected:**
- All seven transport controls are back and the BPM field and **Sync** button are gone.
  This is the regression under test: previously they stayed hidden, and because Animation
  mode does not show the **Sync Mode** selector there was no way to bring them back.
- The record button is enabled again.

## Step: Play forward still works after the round trip

**Action:**
Click the play-forward button.

**Expected:**
- The time readout on the left of the toolbar advances and the playhead moves in the timeline.
- Clicking play-forward a second time stops playback.

## Step: The repaired state survives a reload

**Action:**
Save the project (Ctrl+S), close it, and reopen it.

**Expected:**
- The timeline toolbar shows the transport controls immediately, without opening the
  composition settings.
