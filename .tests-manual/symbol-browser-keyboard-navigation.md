---
id: symbol-browser-keyboard-navigation
title: Symbol Browser Keyboard Navigation
scope: graph-window
tags: [user]
added: 2026-11-04
added-in-version: 4.4
prerequisites:
  - A project you can edit is open in the Graph Window.
related-help:
  - ../.help/docs/using/KeyboardShortcuts.md
---

Checks browsing the symbol browser's categories without the mouse. With an empty search the browser shows
categories, pages and namespaces in columns. The arrow keys move a focus frame between them, Return opens or
inserts the focused entry, and Backspace goes back up.

## Step: The first arrow key shows the focus

**Action:**
Click an empty spot in the graph and press `Tab` to open the symbol browser. Without typing anything, press `Down` once.

**Expected:**
- A frame appears around the top-left entry of the overview (for example `anim...`).
- The focus does not move further on this first press.

## Step: Up and down stay in the column

**Action:**
Press `Down` a few times, then `Up` again.

**Expected:**
- The focus moves one entry at a time within the same column.
- It never jumps sideways to a longer neighbouring column while entries remain in its own column.

## Step: Down continues in the next column

**Action:**
Keep pressing `Down` until the focus reaches the bottom of its column, then press `Down` once more.

**Expected:**
- The focus continues at the top entry of the column to the right.
- Pressing `Up` there returns to the bottom entry of the previous column.

## Step: Left and right move between columns

**Action:**
With an entry in the middle of a column focused, press `Right`, then `Left`.

**Expected:**
- `Right` moves to the entry at about the same height in the next column, never to an entry below the current one in the same column.
- `Left` moves back to the previous column.

## Step: Opening a namespace

**Action:**
Move the focus to a page such as `float...` or a namespace such as `vec2/` and press `Return`.

**Expected:**
- The browser shows that page or namespace.
- The top-left entry of the new view is focused.
- No operator is inserted.

## Step: The focused operator previews in the help window

**Action:**
With the Help Window visible, move the focus onto an operator inside the opened page.

**Expected:**
- The focused operator is highlighted like a selected entry.
- The Help Window shows that operator, as it does when hovering it with the mouse.

## Step: Going back up

**Action:**
Press `Backspace`.

**Expected:**
- The browser returns to the overview.
- The page or namespace you came from is focused again.
- Typing letters now filters the search as usual.

## Step: Inserting with Return

**Action:**
Open a namespace with `Return`, focus an operator and press `Return`.

**Expected:**
- The focused operator is inserted at the placeholder.
- The browser closes.

## Step: The mouse still works

**Action:**
Press `Tab` again, then click a page or namespace with the mouse.

**Expected:**
- The browser opens the clicked entry.
- No focus frame is shown until an arrow key is pressed.

## Step: Moving through search results

**Action:**
Press `Tab` and type `t`, then hold `Down` until the list scrolls, and press `Up` a few times.

**Expected:**
- The highlight moves when a key goes down, not when it is released, and repeats while it is held.
- The list scrolls so the highlighted operator stays visible, with a few rows of context.

## Step: The output follows the end of the chain

**Action:**
Pin the last operator of a chain (nothing connected to its output) to the output window, or show it as background in focus mode. Select it, press `Tab` and insert an operator below it.

**Expected:**
- The output (or background) now shows the new operator.
- Inserting below an operator whose output is already connected, or below one that isn't shown, leaves the output unchanged.
