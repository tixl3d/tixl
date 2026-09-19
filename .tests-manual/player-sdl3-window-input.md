---
id: player-sdl3-window-input
title: Player Window and Input on SDL3 (Windows)
scope: player
tags: [dev, essential, player, platform]
added: 2026-09-18
added-in-version: 4.3
prerequisites:
  - Windows with a Direct3D 11 GPU.
  - The Player was published from this branch before exporting (delete `Player\bin\ReleasePublished`, then `dotnet publish Player\Player.csproj -c Release -p:PublishProfile=FolderProfile`); otherwise the Editor bundles an older Player.
  - A project exported with the Player, whose output reacts to [KeyboardInput] and [MouseInput] (e.g. a key toggling a color, the mouse position moving a shape).
  - For the display steps, a second display.
  - For the German layout step, the German (QWERTZ) keyboard layout is installed.
---

The Player's window, input and fullscreen handling now run on SDL3 instead of WinForms. Rendering is still
Direct3D 11. Every step here passed with the WinForms Player, so any difference is a regression.

## Step: Startup in a window

**Action:**
Start the exported Player, choose a resolution smaller than the display, leave Fullscreen unchecked and press Start.

**Expected:**
- The loading screen (title, status, progress bar) appears in a window centered on the chosen display.
- The window's client area, without title bar and frame, has the chosen resolution.
- The window and the taskbar show the TiXL icon.
- Playback starts after loading; the mouse cursor is visible.

## Step: Window size on a scaled display

**Action:**
In Windows Settings → System → Display, set Scale to 150% (one of the presets, not Custom scaling). Start the Player windowed at 1280 × 720, capture the window with `Win+Shift+S` → Window snip, paste the capture into Paint and read its size.

**Expected:**
- The client area, without title bar and frame, measures 1280 × 720 pixels; the window is not 1.5 times larger.

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
With two displays connected, start the Player. In the startup dialog, open the display list, select the second display, check Fullscreen and press Start.

**Expected:**
- The display list shows the names of the connected monitors. Its numbering comes from SDL and can differ from the numbers in Windows' display settings.
- The Player covers the selected display completely, without borders or a title bar.

## Step: Windowed on the second display

**Action:**
Start the Player windowed on the second display, then press `Alt+Enter` twice.

**Expected:**
- The window opens centered on the second display.
- Fullscreen covers the second display, not the primary one, and the window returns to the same place.

## Step: Keyboard input ops

**Action:**
During playback press and hold keys the project reacts to (letters, digits, arrow keys, `Space`), then release them.

**Expected:**
- The output reacts while each key is held and stops when it is released, as with the previous Player.
- With keyboard playback control enabled in the export settings, `Left`/`Right` jump 4 bars and `Space` pauses and resumes.

## Step: German keyboard layout

**Action:**
Switch Windows to the German layout (`Win+Space`). In a project using [KeyboardInput], test the keys `Z`, `Y`, `ü`, `ö`, `ä`, `ß`, `#`, `+` and `-`.

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

## Step: Operators that need System.Drawing

**Action:**
Export a project that uses [LoadSvg] or [LineTextPoints] and start it.

**Expected:**
- The SVG or text renders as it does in the Editor.
- The log in `.temp/Log` contains no "Failed to set up operator type" error.

## Step: Output setup on two displays

**Action:**
In the Editor, bind two outputs of a project's output setup to the two displays and export it. Start the exported Player.

**Expected:**
- The main window goes fullscreen on the first bound display, a second fullscreen window opens on the other.
- Each output appears on the monitor it was bound to in the Editor.
- Both show their outputs and update in sync.

## Step: Closing

**Action:**
During playback press `Esc`. Start again and close the window with its close button instead.

**Expected:**
- In both cases the Player exits without an error message, and the log in `.temp/Log` ends without errors.
