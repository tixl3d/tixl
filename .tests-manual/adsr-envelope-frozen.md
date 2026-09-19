---
id: adsr-envelope-frozen
title: Freezing an ADSR Envelope
scope: operators
tags: [user, regression]
added: 2026-09-17
added-in-version: 4.3
prerequisites:
  - The _agentTests project is open with an AdsrEnvelope selected.
---

## Step: Hold the current stage

**Action:**
Set Units to Secs, Mode to Gate, and Attack, Decay, and Release to 4 with
Sustain at 0.5. Start playback and set Gate true. Switch Time Mode to Frozen
during Attack. Repeat during Decay, Sustain, and Release.

**Expected:**
- Result holds its current value without a jump when Frozen is selected.
- IsActive holds its current state.
- Editing Envelope, Min, or Max while frozen does not change the held output.

## Step: Discard gate changes while frozen

**Action:**
With an envelope frozen partway through Attack, toggle Gate several times
and finish with Gate true. Wait longer than the envelope would normally last,
then restore the previous Time Mode. Repeat in Trigger mode with Duration 3,
and with an idle envelope frozen before its first trigger.

**Expected:**
- Gate changes do not start Attack or Release while frozen.
- Restoring the clock resumes at the held position without catching up on paused time.
- Triggers received while frozen do not replay when the clock resumes.
- An envelope frozen while idle remains idle after resuming with Gate held true.
- A fresh false-to-true Gate change after resuming starts Attack normally.
- Trigger Duration counts only time spent advancing the envelope.

## Step: Resume with another clock or unit

**Action:**
Freeze an active envelope, change Units, and resume using another Time Mode.
Repeat with Bars and Secs, including Runtime with playback stopped.

**Expected:**
- The first resumed evaluation holds the same position and IsActive value.
- Subsequent evaluations advance using the selected clock and units.
- Reading Result, IsActive, or both does not advance the envelope while frozen.

## Step: Check audio envelopes

**Action:**
Use the existing AudioToneGenerator and PlayAudioSample probes in Gate and
Trigger modes. Check attack, release, and duration while the visual envelope is frozen.

**Expected:**
- Audio envelopes retain their normal timing and trigger behavior.
- Audio envelope controls still use seconds and have no Frozen input.
