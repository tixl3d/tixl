---
id: time-clip-keyframe-insertion
title: Time Clip Keyframes — insertion lands in local time
added: 2026-08-08
added-in-version: 4.3
scope: timeline
prerequisites:
  - A project is open with its own Composition Settings enabled.
  - A short video file (5s or longer) for creating a [VideoClip].
  - Timeline format set to Bars, so the times below are easy to hit. Use snapping.
related-help:
  - ../.help/docs/using/Timeline.md
---

Keyframes are sampled in an op's clip-local time, and they are now also **inserted** there — the
parameter window's keyframe toggle, slider edits on animated parameters, `Shift+C`, and gizmo drags
all convert the playback time into the clip's local time before writing.

All times below are **playback time** (the ruler / playhead position), because that is what you can
observe. The point of every step: a value change made at playback time *t* must also *play back* at
time *t* — no matter how the clip is stretched or slipped.

> The dope sheet, keyset strip, and selection range draw keyframes at their **playback position** —
> a key inside a stretched or slipped clip appears where it takes effect, under the playhead, and
> moves on screen when the clip is stretched or slipped.

## Step: Setup — a clip with identity mapping

**Action:**
1. Move the playtime to the start of the timeline (`Home`).
2. Drag your video file into the timeline's clip area at bar 0, so a `[VideoClip]` is created.
3. Select the clip and open **Edit Clip Times** from its context menu. Set Clip Start to `0`,
   Clip End to `4`. Verify Source Start/End show the same values — `SourceRange == TimeRange`,
   speed 100%.

**Expected:**
- One video clip spanning bars 0–4, playing its footage from the start when the playhead enters it.

## Step: Insert keyframes at bars 1 and 2 (identity — regression)

**Action:**
1. Move the playtime to bar `1`.
2. Select the `[VideoClip]`. In the Parameter Window, `Alt + click` the **Color** input to create
   the first keyframe.
3. Move the playtime to bar `2`. Set Color's alpha to `0`.
4. Play from bar 0 to bar 3.

**Expected:**
- The dope sheet shows keyframes at bars 1 and 2, exactly under the positions where you created them.
- During playback the video fades out between bar 1 and bar 2.

## Step: Stretch the clip — the fade keeps its playback time

**Action:**
1. Drag the clip's **end handle** to bar `8` while holding `Alt` (stretch — the content scales to
   fit; the clip shows ~50% speed in its label).
2. Play from bar 0 to bar 6.

**Expected:**
- The fade-out now happens between playback time **2 and 4** — the animation stretched with the
  content (bars 1–2 of content now occupy bars 2–4 of the timeline).
- The dope sheet shows the two Color keys at bars **2 and 4** — they moved with the stretch.

## Step: Edit a value mid-clip — the change plays back at the same time

**Action:**
1. With the clip still stretched to bars 0–8, move the playtime to bar `6`.
2. In the Parameter Window, drag Color's alpha from `0` up to `1`.
3. Play from bar 4 to bar 8.

**Expected:**
- During playback, the video becomes visible again exactly at bar **6** — the bar where you made
  the edit. (Before this fix, the key was written at content-time 6 and played at bar 12, outside
  the clip.)
- The new key is drawn at bar **6**, directly under the playhead.
- `Ctrl + Z` removes that key again.

## Step: Keyframe indicator agrees with playback

> Note: re-inserting via the indicator is **not** an undo — inserting always captures the *currently
> sampled* value, which after the removal is the value interpolated from the remaining keys. Use
> `Ctrl + Z` to actually restore the removed key.

**Action:**
1. Move the playtime to bar `2` (where the fade-out starts during playback).
2. Look at the keyframe indicator left of the **Color** parameter in the Parameter Window.
3. Click the indicator once (removes the key) and play bars 0–5.
4. Click the indicator again (inserts a new key), and check the new key's value.
5. Press `Ctrl + Z` twice (undo insert, undo remove) and play bars 0–5.

**Expected:**
- At bar 2 the indicator shows "on a keyframe" (filled center).
- After removing: the fade start is gone during playback; the indicator shows no key at bar 2.
- After re-inserting: a key exists at bar 2 again, but with the **current interpolated value** (the
  color the fade had there after removal) — the original fade shape is *not* restored.
- After the two undos: the original fade between bars 2 and 4 plays again.

## Step: `Shift + C` inserts scalar step markers in local time

> `Shift + C` only affects **scalar** parameters — vector parameters like Color are skipped, so
> tapping step markers can't wreck a visible fade animation. Note the displayed *value* of Volume
> never updates (nothing evaluates it yet — video audio is backlog); this step verifies the
> keyframe **positions** in the dope sheet instead.

**Action:**
1. Keep the Color fade from the earlier steps visible in the dope sheet.
2. `Alt + click` the **Volume** input in the Parameter Window to animate it.
3. Move the playtime to bar `3` (clip still stretched to bars 0–8, source 0–4) and press
   `Shift + C` once. Move to bar `5`, press `Shift + C` again.

**Expected:**
- The **Volume** row shows new keyframes drawn at bars **3** and **5** — exactly where you pressed
  `Shift + C`. (Internally they are stored at content bars 1.5 and 2.5; select one and check
  *Clip Times* if curious.)
- The **Color** row gets **no** new keyframes — `Shift + C` skips vector parameters.

## Step: Slipped clip

> With this slip a Color key at content bar `u` plays at bar `(u − 1) × 2`: content 1 → 0,
> content 2 → 2, content 3 → 4.

**Action:**
1. Check the **Color** row in the dope sheet: it should hold exactly two fade keys, drawn at bars
   `2` and `4` (their playback positions while the clip is stretched — content bars 1 and 2). If
   the key from the mid-clip-edit step or other extras are still present, delete them — or apply
   the formula above to your keys instead.
2. Select the clip, open **Edit Clip Times**, and set Source Start to `1`, Source End to `5`
   (Clip Start/End stay 0/8) — the content is slipped by one bar.
3. Play bars 0–6.

**Expected:**
- With the two keys at content bars 1 and 2, the fade-out now plays between bars **0 and 2**.
  (If you kept the content-bar-3 key, the animation extends to bar **4**.)
- Move the playtime to bar `2`: the keyframe indicator shows "on a keyframe".

## Step: Composition opened without a parent path

**Action:**
Open some composition symbol directly from the Symbol Browser (not by entering a clip). `Alt + click`
a parameter of one of its children to insert a keyframe at playtime bar `1`.

**Expected:**
- The key lands at bar 1, exactly under the playhead (identity mapping — no enclosing clip).
- No errors or warnings in the console.
