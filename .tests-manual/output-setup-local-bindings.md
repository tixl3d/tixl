---
id: output-setup-local-bindings
title: Output Setup — Local Bindings, Streams and Help
scope: output-window
tags: [projection-mapping]
added: 2026-09-09
added-in-version: 4.3
prerequisites:
  - A writable project is open whose active setup has one SendToOutput with content, one surface mapped to an output "P1", and one patch (the "Output Setup — Patches" set builds this).
  - The Spout package is loaded (it ships with the editor); a Spout receiver such as the Spout demo receiver is available to verify sending.
  - The graph window and one output window with the Flow Outliner shown are visible; the Help window is open.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers the LOCAL BINDINGS column as an inventory of **plugs**: this machine's displays plus the
Spout / NDI senders it offers, bound to outputs by drag-and-drop or menu, and the help button in
the strip's header. Bindings are machine state: they save immediately and are not undoable.

## Step: Plug rows and the column's plus

**Action:**
With the outliner visible and nothing selected, look at the LOCAL BINDINGS column, then click
the **+** at the right end of its header.

**Expected:**
- One dimmed row per attached display, "Local / Display N" with its resolution as status.
- A menu headed **Add stream sender** lists the loaded stream kinds (at least **Spout**; **NDI**
  when that package is installed).

## Step: Adding a stream plug

**Action:**
Choose **Spout** in the menu. Type `Wall feed` into the name field that opens and press Enter.
Then right-click the new row.

**Expected:**
- A new dimmed row "Wall feed" appears under the displays with "Spout" as its status.
- The context menu offers **Rename** and **Remove stream** (and nothing to duplicate or delete).
- Clicking the row selects it and the Parameter window shows a **Plug** card: its name, a read-only
  **Resolution (px)** (0 × 0 while nothing is bound) and the line "Spout · nothing bound". No
  frame-rate or alpha field, because Spout has no notion of either.

## Step: Binding by drag and drop

**Action:**
1. Drag the "P1" item from OUTPUTS onto the "Wall feed" row.
2. Press Ctrl+Z once.
3. Drag the "Wall feed" row onto the "P1" item.

**Expected:**
- After 1: "Wall feed" is no longer dimmed, a gray curve joins "P1" to it, "P1" loses its
  "unbound" status, and the Parameter window's Output card reads "… px · Spout: Wall feed".
- After 2: nothing changes — bindings are outside the setup's undo stack.
- After 3: the same binding results; dropping in either direction is the same link.

## Step: The stream actually sends

**Action:**
Open a Spout receiver and pick the sender "Wall feed". Then untick **Send** on the Output card
in the Parameter window, wait a moment, and tick it again.

**Expected:**
- The receiver shows P1's composite live.
- With Send off, the sender disappears from the receiver's list (or freezes); with Send on it
  returns under the same name.

## Step: Rename follows through

**Action:**
Double-click the "Wall feed" row, rename it to `Wall 2` and press Enter. Check the receiver.

**Expected:**
- The row, the Output card status and the outliner curve all read the new name.
- The receiver lists "Wall 2" and no longer "Wall feed".

## Step: Binding is a drag, unbinding is the plug's menu

**Action:**
1. Right-click "P1".
2. Drag "P1" onto the "Wall 2" row.
3. Right-click the "Wall 2" row and choose **Unbind P1**, then drag "P1" back onto it.

**Expected:**
- After 1: the menu has no "Bind to" submenu — binding is the drag, and the output's own menu
  stays about the canvas.
- After 2: a curve joins "P1" and "Wall 2"; the receiver picks the sender up.
- After 3: the curve goes and returns.

## Step: Removing a stream plug

**Action:**
Right-click "Wall 2" → **Remove stream**.

**Expected:**
- The row is gone; "P1" shows "unbound" again; the receiver no longer lists the sender.
- Reopening the project keeps it that way (the machine config saved).

## Step: A display binding still presents fullscreen

**Action:**
Drag "P1" onto "Local / Display 2" (or the only display). Then right-click that display row and
choose **Unbind P1**.

**Expected:**
- P1's composite opens fullscreen on that display; the display row lights up with a curve.
- After unbinding, the fullscreen window closes and the row dims.

## Step: Help from the strip's header

**Action:**
Hover the **?** left of the collapse chevron in the strip's header; then click it.

**Expected:**
- While hovered, the Help window previews "Output Setup", an overview of the Board, the Straight
  and Output views, the four columns and how to connect them. (With the Help window's hover
  preview off, the same text shows as a tooltip.)
- Clicking shows the topic in the Help window and brings the window forward.

## Step: Content and surfaces route through a plug

**Action:**
1. Drag the "SendToOutput" row onto the "Wall 2" stream row while nothing is bound to it.
2. Look at the OUTPUTS column and at the row's status.
3. Press Ctrl+Z once, then look at the plug row again.
4. With an output bound to the plug, drag "Surface 1" onto the plug row.

**Expected:**
- After 1: a new output named after the plug appears in OUTPUTS, bound to it, showing the
  send full-canvas — the plug stands for what it presents, so the drop created the missing
  canvas rather than doing nothing.
- After 2: the plug is no longer dimmed and a curve joins the new output to it.
- After 3: the undo removes the created output; the plug reads as free again. (The binding
  itself is machine state and is not undone, so it simply points at nothing.)
- After 4: no second output is created — "Surface 1" is mapped onto the output already bound
  to that plug.

## Step: A stream plug's settings

**Action:**
1. Add an **NDI** stream plug (needs the NDI package) and select it.
2. Set **Frame rate** to 30 and tick **Send alpha**.
3. Bind an output to it and look at **Resolution (px)**.
4. Reopen the project and select the plug again.

**Expected:**
- After 1: the Plug card shows **Frame rate** and **Send alpha** in addition to the name and
  resolution — NDI honours both, so both are offered.
- After 2: an NDI receiver reports the stream at 30 fps with alpha; the edits take effect while
  it runs, without re-adding the plug.
- After 3: the resolution reads the bound output's canvas size, and the line names that output.
- After 4: both settings are still there (they live in the machine config).
