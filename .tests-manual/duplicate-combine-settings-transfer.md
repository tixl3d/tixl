---
id: duplicate-combine-settings-transfer
title: Duplicate carries project settings; Combine moves snapshots
added: 2026-08-28
added-in-version: 4.3
scope: graph
prerequisites:
  - A writable user project with at least one composition that has project settings enabled (a main soundtrack assigned and a non-default BPM, e.g. 128).
  - A short audio file (mp3/wav) available in the project's assets for the soundtrack.
---

**Duplicate as New Type** now copies the source symbol's project settings (soundtrack, BPM, audio
mix, export and proxy configuration) and its per-symbol editor settings (timeline view, render and
recording setup, window layout). **Combine into New Type** now moves the snapshot data of the
selected ops into the new symbol's variation pool.

## Step: Duplicate copies project settings

**Action:**
Open a composition that has project settings enabled: open **Project Settings**, confirm a main
soundtrack file is assigned and set **BPM** to `128`. Close the settings window. In the parent graph,
select the composition's op, right-click → **Duplicate as New Type...**, name it `DupSettingsTest`,
and confirm.

**Expected:**
- A `DupSettingsTest` op appears in the graph next to the original.

**Action:**
Enter the new `DupSettingsTest` op (double-click) and open **Project Settings**.

**Expected:**
- **BPM** shows `128` (not the 120 default).
- The same soundtrack file is assigned as main soundtrack; the timeline shows its background
  waveform, and pressing play produces audio.

## Step: Duplicate copies render settings and timeline view

**Action:**
Go back to the source composition. Open the **Render** window and set **End** to a non-default value
(e.g. `16` bars). Duplicate the composition again as `DupSettingsTest2`, enter it, and open the
**Render** window.

**Expected:**
- **End** shows `16` bars, matching the source instead of the 8-bar default.

## Step: Combine moves snapshots

**Action:**
In a writable composition, place two value-animated ops (e.g. two [Value] ops feeding something
visible). Enable both for snapshots (parameter window → snapshot toggle per op). Open the
**Snapshots** window and save two snapshots with visibly different values (e.g. snapshot 1 with both
values at `0`, snapshot 2 with both at `1`). Confirm clicking the two snapshots toggles the values.

Now select only the two [Value] ops, right-click → **Combine into New Type...**, name it
`CombineSnapTest`, and confirm.

**Expected:**
- The two ops are replaced by a single `CombineSnapTest` op.
- The parent's **Snapshots** window no longer lists the two snapshots (they only covered the
  combined ops and were moved, not duplicated).

**Action:**
Enter the `CombineSnapTest` op and open the **Snapshots** window.

**Expected:**
- Both snapshots are listed with their original names and order.
- Clicking each snapshot restores the corresponding values on the copied [Value] ops (`0` / `1`).

## Step: Combine cleans up snapshots that lost all their content

**Action:**
In a fresh composition, place two snapshot-enabled [Value] ops and save one snapshot covering both.
Delete one of the two ops with `Del` (the snapshot now half-dangles). Then select the remaining op
and combine it into a new type.

**Expected:**
- After the combine, the parent's **Snapshots** window is empty: the snapshot's last remaining
  entry moved into the combined op, so the leftover shell was removed.
- Inside the combined op, the snapshot is listed and restores the copied op's value.

## Step: Combine keeps unrelated snapshot data in the parent

**Action:**
Repeat the combine setup, but this time with three snapshot-enabled ops. Save one snapshot covering
all three. Combine only two of the ops into a new type.

**Expected:**
- The parent still lists the snapshot; activating it still restores the third (uncombined) op's
  value.
- Inside the combined op, the moved snapshot restores the two copied ops' values.
