# Gamepad graph editing (parked)

Editing graphs from a game controller during live performances: navigating, inserting operators, tweaking
parameters, steering the camera. Explored on 2026-11-04/05 to a working proof of concept, then **parked**: the
feature spreads over too much of the editor for how fringe it is. This file keeps what was learned for the day it
gets picked up again.

**Where the code is:** branch `feat/gamepad-control`, commit `ba18f1ec8` ("Gamepad control graph
(proof-of-concept)") on top of `89e622dda`. Manual test set: `.tests-manual/gamepad-editor-control.md` on that
branch. What was worth keeping regardless moved to the main line (see the end).

## Verdict

- **Text input is the real burden.** Searching operators and filtering parameters is typing. Suggestions,
  presets and a namespace drill cover a lot (about 90 % of inserts are picks from the type-filtered suggestions),
  but anything off the beaten path needs a keyboard.
- **Graph editing stays clumsy.** Moving a selection with the d-pad, inserting on a side, dragging snapped groups:
  all possible, none as fluent as mouse and keyboard. A better infinity slider on the stick doesn't change that.
- **Camera control is the real use case.** It integrates through `ICameraManipulator` next to the SpaceMouse, with
  no graph hooks, and fits teamwork: one person models, the other flies. It shipped separately (see
  `Plan_GamepadControl.md`).
- Possible future angle: accessibility, by abstracting editor commands into gestures that any device (pad, foot
  switch, MIDI controller) can trigger.

## What the proof of concept did

Usage indicator in the menu bar cycles Operators → Editor → Off (`UserSettings.GamepadUsage`); in Editor mode the
`[Gamepad]` op sees a released pad.

| Context | Input | Action |
|---|---|---|
| Graph | D-pad | Move selection like the arrow keys (repeat 400 ms, then 100 ms) |
| | A | Edit parameters (popover); without a selection, insert in the view centre |
| | B | Clear selection; with nothing selected, go to the parent composition |
| | Hold X + d-pad, release X | Insert on the picked side (down/right after the output, up/left before the input), previewed while held |
| | Y | Pin to output; hold to only peek (= P) |
| | LB / RB | Undo / redo (started as bookmark stepping) |
| | Left stick | Pan (quadratic, 375 px/s; 1500 was far too fast) |
| | Right stick pressed + tilt | Move the selected op's snapped group (one undo step, no unsnap/snap) |
| | Menu / Share | Play-pause / focus mode |
| | View | Switch to camera steering (View or B returns) |
| Symbol browser | D-pad, A or X, B | Arrows, Enter, Esc (Backspace inside the namespace drill) |
| | Y | Presets of the highlighted operator (the typed space); B back to the list |
| | LB / RB | Previous / next page of the overview |
| Parameter popover | D-pad | Arrows (incl. hold-to-nudge) |
| | A / B / X / Y | Apply and stay (toggles bools) / apply and close / insert into input (Shift+I) / reset (Shift+R) |
| | Right stick | Numbers: X value (cubic, 0.15 ranges/s), Y range in decades (1.5/s, kept until restart); vectors: x/y, left stick y = z; colors: hue/saturation, left stick brightness/alpha |
| | LT / RT | Range ×0.1 … ×10, continuous |

Feedback from use: A closing the popover was confusing (hence apply-and-stay); the auto-opening parameter popup
after inserting in focus mode got in the way; the result list had to move on press with repeat and scroll the
selection into view; Y ended up meaning "forward" in the browser but "revert" in the popover (unresolved).

## Architecture lessons

- **Good:** feeding pad buttons into existing `UserActions` (`KeyActionHandling.TriggerForThisFrame`) made arrow
  navigation, Edit Parameter, playback, focus mode, peek and undo work with no per-feature code.
- **Bad:** contexts built around the keyboard (browser, popover) got injected ImGui key events *and* direct pad
  reads. The injection works (with care: buttons held when a context opens are ignored until released, injected
  keys are released on context change) but is a hack, and the direct reads put `GamepadControl` checks into nine
  shared files.
- **If revived, consolidate first:** semantic actions (Confirm, Back, More, Reset, NextPage, PreviousPage) as
  `UserActions` that the browser and popover check, bindable from keyboard, pad, MIDI. The pad becomes one mapping
  table; no injection, no hooks. That is also the base for the accessibility/gesture idea.
- Analog editing needed its own slider entry point (`InfinitySliderOverlay.DrawForStick`) and a stick path for
  vectors and colors that duplicates the preview-command setup of `StepValue`; share that if revived.
- Hardware: the Xbox Series controller over Bluetooth never delivered input on Arch with the stock
  `hid_microsoft` driver (reconnect loop, blinking LED); USB works. Try `xpadneo` before debugging code.

## Moved to the main line

- `IoServices/SdlGamepads` (SDL3 gamepads, Linux support for `[Gamepad]`) and the usage indicator.
- `UiHelpers/FocusNavigator`: keyboard navigation of the symbol browser's namespace drill.
- Browser result list: moves on key press with repeat, reveals the highlighted row through the list clipper.
- Inserting after the shown end of a chain moves the output (or focus-mode background) to the new operator.
- `Camera/GamepadCamera` (camera steering), without the View toggle: the usage indicator selects it.

## Open ideas

Gain/bias control UI on the sticks; a visible color picker while editing colors on the sticks; group drag with
unsnap/splice/snap like the mouse; one consistent meaning for Y.
