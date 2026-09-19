---
id: adsr-envelope-time-modes
title: ADSR Envelope Time Modes and Units
scope: operators
tags: [user, regression]
added: 2026-09-17
added-in-version: 4.3
prerequisites:
  - The _agentTests project is open.
  - AdsrEnvelope and Time probes are available; Time has SpeedFactor 1.
  - AudioToneGenerator and PlayAudioSample feed evaluated AudioBus operators at a low listening volume.
---

## Step: Check existing graphs and defaults

**Action:**
Open an envelope graph saved before Time Mode and Units were added. Check its
Mode, values, and connections. Compare Result and IsActive against the same
gate sequence in the earlier version. Add a new AdsrEnvelope.

**Expected:**

- Existing Mode values remain Gate = 0 and Trigger = 1; connections are preserved.
- Missing timing inputs use LocalIdleMotionFxTime and Bars.
- Default timing, output range, and IsActive match the earlier version.
- The new envelope also defaults to LocalIdleMotionFxTime and Bars.

## Step: Save the dropdown selections

**Action:**
Set Mode to Trigger, Time Mode to PlaybackTime, and Units to Secs. Save the
project, reload it, and inspect the inputs. Repeat with Frozen and Bars.

**Expected:**

- Each selection survives save/reload independently.
- Changing Time Mode does not change Gate/Trigger Mode or its connections.

## Step: Compare clock sources

**Action:**
Compare AdsrEnvelope with Time configured to the matching clock and units.
Exercise every row below in both Bars and Secs, first with playback running,
then stopped. Include Idle Motion and local time remapping.

| Time Mode | Expected source |
| --- | --- |
| LocalIdleMotionFxTime | Local effect time, including Idle Motion and local effect-time remapping |
| LocalTime | Local timeline time, including local remapping, without Idle Motion |
| PlaybackTime | Playback timeline, unaffected by local remapping |
| Runtime | Application runtime, advancing even when playback is stopped |
| Frozen | Holds the envelope state; Time itself returns zero in this mode |

**Expected:**

- For the four advancing modes, envelope progress follows changes in the matching Time output.
- Local remapping affects the local clocks, not PlaybackTime or Runtime.
- Frozen follows the separate [Frozen envelope checks](adsr-envelope-frozen.md), including ignoring gate changes and resuming without catch-up.

## Step: Measure the selected units

**Action:**
Set Time Mode to PlaybackTime and Mode to Gate. Use Attack 1, Decay 1,
Sustain 0.25, Release 1, Min 2, and Max 6. Measure each stage in both units
at fixed 120 BPM and 90 BPM. Use small playback increments or continuous
playback; large timeline jumps are covered separately below.

**Expected:**

| BPM | Units | Approximate duration of each A/D/R stage |
| --- | --- | --- |
| 120 | Bars | 2 seconds |
| 120 | Secs | 1 second |
| 90 | Bars | 2.667 seconds |
| 90 | Secs | 1 second |

- Allow a frame of rounding at each stage transition.
- Result starts at 2, peaks at 6, sustains at 3, and releases to 2 in every case.
- Sustain and Min/Max do not change meaning when Units changes.
- Attack, Decay, and Release field tooltips display the selected unit.

## Step: Check Gate mode

**Action:**
In each unit, raise Gate, hold through Attack and Decay, then lower Gate.
Repeat with Gate lowered during Attack and during Decay. Change Duration
while Gate remains true.

**Expected:**

- A rising edge starts Attack; holding Gate true sustains the envelope.
- A falling edge starts Release from the current level.
- Duration does not release the envelope in Gate mode.
- IsActive stays true through Release and becomes false when it finishes.

## Step: Check Trigger mode and Duration

**Action:**
Set Mode to Trigger and Duration to 1. Pulse Gate, then repeat while holding
Gate true. Measure the delay until Release starts at both BPM values and
both units from the timing table. Send another pulse during the envelope.
Repeat with Duration 0 and a negative value.

**Expected:**

- A falling edge does not release the envelope in Trigger mode.
- Duration is measured from the rising edge and uses the timing table above.
- Release follows Duration; its length is additional to Duration.
- Another rising edge restarts Attack and the Duration timer.
- Zero or negative Duration disables automatic release.

## Step: Change clocks, units, tempo, and timeline position

**Action:**
During a long Attack, change Time Mode or Units without changing Gate/Trigger
Mode. Seek backward and forward, and change BPM in Secs mode. Also run the
separate Frozen test set for entering/leaving Frozen and resuming on a different clock.

**Expected:**

- Mode and all envelope parameter values remain unchanged.
- Switching Units reinterprets timing values; it does not rescale them.
- Secs conversion uses the current BPM, as Time does; it is not a tempo-integrating clock.
- A negative clock delta or one greater than 1 selected unit advances the calculator by its existing 0.016-unit fallback. Large seeks are not equivalent to continuous playback.
- These existing discontinuity rules do not apply to Frozen resume: the first resumed evaluation synchronizes time without advancing.

## Step: Read either output and repeat a timestamp

**Action:**
Run matching envelopes with only Result evaluated, only IsActive evaluated,
and both outputs evaluated. While partway through a stage, stop the selected
clock and repeatedly read both outputs. Repeat near a stage boundary.

**Expected:**

- Either output evaluates the envelope and updates both stored output values.
- Repeated reads at one timestamp add no elapsed time.
- Existing stage-transition behavior is retained: another read may apply a pending transition or update the displayed boundary value without adding time.
- IsActive remains consistent with the current stage.

## Step: Check multiple envelope controls

**Action:**
Show several AdsrEnvelope nodes together. Hover and drag their envelope
handles. Select envelopes using different Units, including one with Units
connected to another operator.

**Expected:**

- Controls affect only the intended envelope; no ImGui ID conflict warning appears.
- Each envelope's A/D/R tooltips use its own current units.
- Sustain remains a unitless level.

## Step: Check audio compatibility

**Action:**
Exercise AudioToneGenerator and PlayAudioSample through their AudioBus probes
in Gate and Trigger modes. Enable UseEnvelope for PlayAudioSample. Check a
held gate, an early release, a short trigger pulse, and Duration-controlled
release. Freeze the visual AdsrEnvelope while the audio probes run.

**Expected:**

- Audio attack, release, and duration behavior match the earlier version.
- Freezing the visual envelope does not freeze or retrigger audio envelopes.
- Both audio envelope widgets retain seconds tooltips and have no Time Mode or Units inputs.
- Finish with triggers off and the audio silent; leave the probes for inspection.
