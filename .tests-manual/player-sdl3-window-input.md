---
id: player-sdl3-window-input
title: Player Window and Input on SDL3 (Windows)
scope: player
tags: [player, platform]
added: 2026-09-18
added-in-version: 4.3
prerequisites:
  - Windows with a Direct3D 11 GPU.
  - A project exported with the Player, whose output reacts to [KeyboardInput] and [MouseInput] (e.g. a key toggling a color, the mouse position moving a shape).
  - For the German layout step, the German (QWERTZ) keyboard layout is installed.
  - For the multi-display step, a second display and a project with an output setup that binds two outputs to two displays.
---

The Player's window, input and fullscreen handling now run on SDL3 instead of WinForms. Rendering is still
Direct3D 11. Every step here passed with the WinForms Player, so any difference is a regression.

## Step: Startup in a window

**Action:**
Start the exported Player, choose a resolution smaller than the display, leave Fullscreen unchecked and press Start.

**Expected:**
- The loading screen (title, status, progress bar) appears in a window centered on the chosen display.
- The window's client area has exactly the chosen resolution, also on a display scaled to 150% or 200%.
- The window and the taskbar show the TiXL icon.
- Playback starts after loading; the mouse cursor is visible.

## Step: Cancel loading

**Action:**
Start the Player again and press `Esc` while the loading screen is shown.

**Expected:**
- Loading stops and the Player exits without an error message.

## Step: Fullscreen toggle

**Action:**
During playback press `Alt+Enter`, wait, then press `Alt+Enter` again.

**Expected:**
- The first press turns the window into a borderless window covering the whole display; the cursor hides.
- The second press restores the previous window size and position; the cursor shows again.
- The image never freezes, flickers black for more than a frame, or stretches wrongly after either toggle.

## Step: Start fullscreen on a chosen display

**Action:**
With two displays connected, start the Player, select the second display, check Fullscreen and press Start.

**Expected:**
- The Player covers the second display completely, without borders or a title bar.

## Step: Keyboard input ops

**Action:**
During playback press and hold keys the project reacts to (letters, digits, arrow keys, `Space`), then release them.

**Expected:**
- The output reacts while each key is held and stops when it is released, as with the previous Player.
- With keyboard playback control enabled in the export settings, `Left`/`Right` jump 4 bars and `Space` pauses and resumes.

## Step: German keyboard layout

**Action:**
Switch Windows to the German layout. In a project using [KeyboardInput], test the keys `Z`, `Y`, `ü`, `ö`, `ä`, `ß`, `#`, `+` and `-`.

**Expected:**
- Each key triggers the same key code it triggered with the WinForms Player (`Z` and `Y` are not swapped).

## Step: Keys released on focus loss

**Action:**
Hold a key the project reacts to, press `Alt+Tab` to another application while still holding it, release it there, then return to the Player.

**Expected:**
- The Player no longer treats the key as held.

## Step: Mouse input ops

**Action:**
Move the mouse over the Player window, then press and hold the left button while moving.

**Expected:**
- [MouseInput] follows the pointer, 0..1 from the window's top-left to bottom-right corner.
- Its left-button output is on while the button is held and off after release.

## Step: Output setup on two displays

**Action:**
Start a Player exported with an output setup that binds two outputs to two displays.

**Expected:**
- The main window goes fullscreen on the first bound display, a second fullscreen window opens on the other.
- Both show their outputs and update in sync.

## Step: Closing

**Action:**
During playback press `Esc`. Start again and close the window with its close button instead.

**Expected:**
- In both cases the Player exits without an error message, and the log in `.temp/Log` ends without errors.
