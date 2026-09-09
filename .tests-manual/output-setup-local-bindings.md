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
- Clicking the row selects nothing and shows nothing in the Parameter window.

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

## Step: Binding from the menus

**Action:**
1. Right-click "P1" → **Bind to**.
2. Choose **Unbind**, then in the same submenu choose **Send to Spout: Wall 2**.
3. Right-click the "Wall 2" row.

**Expected:**
- After 1: the submenu lists **Fullscreen on Display N (…)** per display, **Send to Spout:
  Wall 2** (ticked while bound), and **Unbind**.
- After 2: the curve goes and returns.
- After 3: the row's menu now also offers **Unbind P1**.

## Step: Removing a stream plug

**Action:**
Right-click "Wall 2" → **Remove stream**.

**Expected:**
- The row is gone; "P1" shows "unbound" again; the receiver no longer lists the sender.
- Reopening the project keeps it that way (the machine config saved).

## Step: A display binding still presents fullscreen

**Action:**
Drag "P1" onto "Local / Display 2" (or the only display). Then right-click "P1" → **Bind to**
→ **Unbind**.

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
