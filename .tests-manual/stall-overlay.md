---
id: stall-overlay
title: Progress Overlay During Long Operations
added: 2026-09-17
added-in-version: 4.3
scope: editor-shell
tags: [essential]
prerequisites:
  - An editable project with at least one operator of its own is open.
  - For the simulated stalls, the editor was started with `--debug-server 9042` and a
    client can send JSON lines to that port (see `.agentic/DEBUG_PROTOCOL.md`).
related-help:
  - ../.help/docs/advanced/WritingCodeOps.md
---

While the main thread is busy for longer than about half a second, a second thread keeps the
window alive: it shows the last frame dimmed, with a panel naming the activity. These steps check
that the overlay appears for real stalls only and that the editor recovers cleanly.

## Step: Overlay appears for an unlabeled stall

**Action:**
Send `{"id":"1","method":"stallMainThread","seconds":6}` to the debug server and watch the editor window.

**Expected:**
- After roughly half a second the interface dims smoothly and a centered panel titled `Working...` appears.
- A short segment slides along the bar, and a seconds counter on the right counts up.
- The line below the bar shows the latest log message.
- The window title bar never shows "Not Responding" and the window content never turns white.
- After six seconds the editor is back to normal without a flash of garbage or a black frame.

## Step: Estimated progress on the second run

**Action:**
Send `{"id":"2","method":"stallMainThread","seconds":5,"estimateKey":"manual-test","message":"Testing estimate..."}` twice, waiting for the first to finish.

**Expected:**
- The first run shows the title `Testing estimate...` with the sliding segment, because no estimate exists yet.
- The second run shows a bar that fills quickly at first, then slows down, and never reaches the end.
- During the second run no seconds counter is visible until the stall outlasts the estimate.

## Step: Real work shows its own message

**Action:**
Duplicate one of your own operators with *Duplicate as New Type...*, or create a new project from the project hub.

**Expected:**
- If the operation takes longer than about a second, the panel names it, e.g. `Duplicating as MyOp2...` and then `Compiling MyProject...`.
- If it finishes faster, no overlay is visible at all.
- The operation completes as it did before; the new operator or project appears.

## Step: Modal interaction is not a stall

**Action:**
Drag the editor window by its title bar and hold it for a few seconds. Then open any native file dialog, for example while importing an asset, and leave it open for a few seconds.

**Expected:**
- No overlay appears in either case.

## Step: Resize after a stall

**Action:**
Trigger another six second stall. While the overlay is visible, do nothing. Once the editor is back, resize the window and maximize it.

**Expected:**
- The interface redraws correctly at the new size.
- Triggering one more stall at the new size shows an undistorted frozen frame with the panel centered.

## Step: Second view stays clean

**Action:**
Enable the second output window (*Windows → Output Window*), show an output, and trigger a six second stall.

**Expected:**
- The overlay appears on the main window only.
- The second window keeps showing its last image and never shows the panel.
