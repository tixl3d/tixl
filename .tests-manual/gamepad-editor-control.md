---
id: gamepad-editor-control
title: Game Controller Editor Control
scope: graph-window
tags: [user]
added: 2026-11-05
added-in-version: 4.4
prerequisites:
  - A game controller is connected (on Linux, use USB if Bluetooth pads stay silent).
  - The controller icon in the menu bar is switched to "Game controller drives the editor" (green).
  - A project you can edit is open, with a few connected operators.
---

Checks the discrete game controller mapping: selecting and inserting operators, browsing the symbol browser,
editing parameters, bookmarks, playback and focus mode. Button names follow the Xbox layout.

## Step: Moving the selection with the d-pad

**Action:**
Select an operator in a snapped stack, then press d-pad down a few times and hold it for a second.

**Expected:**
- Each press selects the next operator below, as the Down arrow key does.
- Holding the d-pad repeats the move after a short delay.

## Step: Inserting with X

**Action:**
With one operator selected, press and hold X, then release it.

**Expected:**
- While X is held, the selected operator's bottom edge (or right edge, if its output is snapped to the right) is highlighted with a faint outline of the new operator beside it.
- On release the symbol browser opens there, filtered to fitting operators.

## Step: The output follows the end of the chain

**Action:**
Show the last operator of a chain (nothing connected to its output) in the output with Y, or as the background in focus mode. With it selected, press X, release it and insert an operator.

**Expected:**
- The output (or background) now shows the new operator.
- Inserting after an operator whose output is already connected, or after one that isn't shown, leaves the output unchanged.

## Step: Picking the insert side

**Action:**
Hold X and press d-pad up, right, left and down in turn, watching the preview, then release X after d-pad left.

**Expected:**
- The highlighted edge and outline follow the d-pad: up and left before the operator's input, down and right after its output.
- The selection does not move while X is held.
- On release the browser opens before the operator's input, filtered to operators that fit it.

## Step: Picking a suggestion

**Action:**
Press d-pad down twice, then hold it until the list scrolls, then press A.

**Expected:**
- The highlighted suggestion moves down as the d-pad goes down, and repeats while it is held.
- The list scrolls so the highlighted entry stays visible, also when the browser opens with X on an operator whose best match is further down.
- A inserts the highlighted operator and closes the browser. X does the same.
- In focus mode no parameter popup opens over the new operator; A edits its parameters when wanted.

## Step: Inserting with a preset

**Action:**
Open the browser with X on an operator, highlight an operator that has presets, press Y, pick a preset with the d-pad, then press B, Y again and A.

**Expected:**
- Y switches the list to the highlighted operator's presets, with "Default" first, like typing a space.
- B returns to the operator list without closing the browser.
- A inserts the operator with the picked preset applied.

## Step: Browsing namespaces

**Action:**
Press B until nothing is selected, then press A. Use the d-pad to move the focus frame and A to open a page.

**Expected:**
- The browser opens in the middle of the view with the namespace overview.
- The d-pad moves the focus frame like the arrow keys; A opens the focused page.

## Step: Stepping through pages with the shoulder buttons

**Action:**
Inside the opened page, press RB a few times, then LB.

**Expected:**
- RB opens the next page or namespace of the overview, LB the previous one.
- The header next to the placeholder shows the current namespace.

## Step: Going back with B

**Action:**
Press B, then B again.

**Expected:**
- The first B returns to the overview with the page you came from focused.
- The second B closes the browser without inserting anything.

## Step: Panning the graph

**Action:**
In the graph, tilt the left stick slightly in each direction, then push it fully.

**Expected:**
- The view moves in the direction of the stick: a light tilt slowly, full deflection across a screen in a few seconds.
- Releasing the stick stops the view after a short glide.

## Step: Editing parameters

**Action:**
Select an operator with number parameters, press A, then use d-pad down and up, hold d-pad right for a moment, press A, then B.

**Expected:**
- A opens the parameter popover without immediately closing it again.
- D-pad up and down move between parameters.
- Holding d-pad right nudges the focused number with the infinity slider.
- A applies the value and keeps the popover open; on a bool parameter A toggles it.
- B applies and closes the popover.

## Step: Adjusting a value with the right stick

**Action:**
Open the popover on a number parameter with A, tilt the right stick slightly right, then fully left, then diagonally. Repeat while holding the left trigger, then while holding the right trigger. Press A.

**Expected:**
- The infinity slider appears; a light tilt changes the value in small steps, full deflection quickly.
- Tilting the stick up widens the slider's range in powers of ten, down narrows it, also while it is tilted sideways; the range stays when the stick is released.
- Holding the left trigger makes the steps finer, the right trigger coarser.
- A applies the value.

## Step: Vectors and colors on the sticks

**Action:**
In the popover, move to a Vector2 or Vector3 parameter and tilt the right stick, then the left stick up. Move to a color parameter and tilt both sticks in all directions. Press A.

**Expected:**
- On a vector the right stick changes x (sideways) and y (up and down) together; the left stick's up and down changes z.
- On a color the right stick changes hue (sideways) and saturation (up and down), the left stick brightness (up and down) and alpha (sideways).
- A applies the change as one undo step.

## Step: Resetting a parameter

**Action:**
In the popover, change a parameter with the right stick, press A, then press Y.

**Expected:**
- Y resets the focused parameter to its default, as Shift+R does; one undo brings the changed value back.

## Step: Inserting into a parameter

**Action:**
In the popover, focus a parameter that isn't connected and press X.

**Expected:**
- The symbol browser opens connected to that input, as with Shift+I.

## Step: Moving a snapped group

**Action:**
Select one operator of a snapped stack, press the right stick down and keep it pressed while tilting it, then release. Undo with Ctrl+Z.

**Expected:**
- The whole snapped stack moves with the stick, not only the selected operator.
- Releasing the stick puts it down; one undo moves the whole stack back.

## Step: Showing an operator in the output with Y

**Action:**
Select an operator that isn't shown in the output, tap Y. Then select another one, hold Y for a second and release it.

**Expected:**
- The tap shows the operator in the output (in focus mode as the graph background) and it stays there.
- Holding Y shows the other operator only while Y is held; on release the previous one returns.

## Step: Deselecting and going up

**Action:**
Open a composition (for example with a double click), select an operator inside it, then press B twice.

**Expected:**
- The first B clears the selection.
- The second B returns to the parent composition.

## Step: Undo and redo on the shoulder buttons

**Action:**
In the graph, insert an operator with the controller, press LB, then RB.

**Expected:**
- LB undoes the insertion, RB redoes it.

## Step: Playback and focus mode

**Action:**
Press Menu (☰), then Share, then Share again.

**Expected:**
- Menu starts playback; pressing it again stops it.
- Share toggles focus mode on and off.

## Step: Steering the camera

**Action:**
Show a scene with a camera in the output window (or, in focus mode, select the camera operator so the background shows it). Press View (⧉), move both sticks and pull both triggers, then press View again.

**Expected:**
- A toast says the controller steers the camera.
- The left stick flies sideways and forward, the right stick looks around, the right trigger lifts the camera and the left trigger lowers it; a light touch moves gently.
- Looking straight up or down stops before the view flips.
- The graph doesn't pan or change its selection meanwhile.
- View (or B) returns to the graph with a toast.

## Step: Operators stay quiet

**Action:**
While the controller drives the editor, watch a [Gamepad] operator's output and press some buttons.

**Expected:**
- The operator's outputs stay at rest.
