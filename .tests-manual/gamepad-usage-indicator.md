---
id: gamepad-usage-indicator
title: Game Controller Usage Indicator
scope: io
tags: [user]
added: 2026-11-05
added-in-version: 4.4
prerequisites:
  - A game controller (Xbox, PlayStation or Switch) is at hand but not yet connected.
  - A project with the [GamePadExample] operator is open, or a [Gamepad] operator whose State output is shown in the Output Window.
---

Checks that game controllers are read through SDL on every platform, and that the menu bar indicator decides
whether the controller drives operators, the camera of the output, or nothing.

## Step: The indicator appears with a controller

**Action:**
Look at the menu bar, then connect the controller.

**Expected:**
- Before connecting, no controller icon is shown in the menu bar.
- Within a second of connecting, a small controller icon appears next to the outputs indicator.
- Hovering it shows the controller's name and "Game controller drives operators".

## Step: The indicator flashes on input

**Action:**
Press a few buttons and move a stick.

**Expected:**
- The icon lights up with each input and fades back to a dim resting state shortly after.

## Step: Operators receive the controller

**Action:**
Move the left stick and press A while watching the [Gamepad] operator's State output.

**Expected:**
- `LeftThumbX` and `LeftThumbY` follow the stick, with up being positive.
- `Buttons/A` is 1 while the bottom face button is held.
- A resting stick reports exactly 0.

## Step: Switching the controller to the camera

**Action:**
Click the controller icon once.

**Expected:**
- A toast says "Game controller steers the camera" and the icon turns green.
- The [Gamepad] operator's outputs stay at rest while the stick moves, and the operator shows a warning that the editor uses the controller.

## Step: Flying the camera

**Action:**
With the controller steering the camera, show a 3D scene in the output window (in focus mode: select its camera operator so the background shows it). Move both sticks and pull both triggers.

**Expected:**
- The left stick flies sideways and forward, the right stick looks around, the right trigger lifts the camera and the left one lowers it.
- A light touch moves gently; full deflection is fast but controllable.
- Looking straight up or down stops before the view flips.
- The graph doesn't react to the controller.

## Step: Input device settings

**Action:**
Open Settings → Input Devices. Raise Camera Smoothing to 1.5, fly the camera with a quick stick flick, then set it to 0 and flick again. Try Invert Look and Look Speed.

**Expected:**
- The Game Controller group lists the connected controller with a green dot; with none connected it says so, and Rescan finds a newly plugged one.
- "Use for" shows the same choice as the menu bar icon and changes it.
- With high smoothing the camera eases in and glides out after the stick is released; at 0 it follows the stick directly.
- Invert Look flips up and down on the right stick; Look Speed and Move Speed scale the camera.
- The SpaceMouse group shows its status (on Linux: only supported on Windows) and its settings.

## Step: Switching the controller off

**Action:**
Click the icon again, then once more.

**Expected:**
- The first click shows "Game controller is off" and dims the icon; it no longer flashes on input.
- The second click returns to "Game controller drives operators" and the operator follows the stick again.

## Step: The choice survives a restart

**Action:**
Switch to "steers the camera", restart TiXL and connect the controller.

**Expected:**
- The icon is green and the tooltip says "Game controller steers the camera".

## Step: Disconnecting

**Action:**
Disconnect the controller.

**Expected:**
- The icon disappears.
- The [Gamepad] operator reports that the controller is not connected.
