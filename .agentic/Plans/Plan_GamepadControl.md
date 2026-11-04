# Gamepad control (editor UI from a game controller)

Drive the editor from a game controller during live performances, mostly in focus mode: move through the
graph, insert ops from the symbol browser's suggestions, tweak parameters with an analog stick, jump between
bookmarks and steer the background camera. It is an expert tool. People using it know the editor well, so
action toasts are enough feedback and invisible modes are acceptable.

**Status (2026-11-04):** Phase 0 done (`4063e1378`): `UiHelpers/FocusNavigator` drives the symbol browser's
namespace drill with the arrow keys (Return opens or inserts, Backspace goes up and refocuses the entry it came
from). Moves keep their axis: up/down stay in the column and only continue into the neighbouring column past its
end; left/right go to the closest-height entry of the next column. Possible refinement: a remembered goal height
for left/right if offset rows make the focus drift. Test set: `.tests-manual/symbol-browser-keyboard-navigation.md`.
Phase 1 implemented, verified on Linux with an Xbox Series controller over USB (`[Gamepad]` dict follows the pad, hot-plug works): `IoServices/SdlGamepads` (SDL3, background events on,
radial dead zones, 0.5 s hot-plug scan, initialised by Editor and Player), `[Gamepad]` reads it instead of
XInput, `Gamepad/GamepadIndicator` in the menu bar cycles `UserSettings.GamepadUsage` (Operators → Editor → Off).
`XInputGamepad` and the SharpDX.XInput package stay until main's calibration is ported. Test set:
`.tests-manual/gamepad-usage-indicator.md`.
Phase 2 implemented, untested: `Gamepad/GamepadControl` triggers user actions in the graph through
`KeyActionHandling.TriggerForThisFrame` (new actions `SelectPrevious/NextOperatorBookmark`), and while the symbol
browser or parameter popover is open it injects ImGui key events (d-pad → arrows, A → Enter, B → Esc or Backspace
inside the drill). X inserts after the selection or in the view centre without needing the mouse over the graph.
After the first test: A without a selection inserts in the view centre, Y = P (tap pins to output, hold peeks),
the left stick pans the graph (quadratic, 1500 px/s), and the right stick drives the popover's infinity slider
(`InfinitySliderOverlay.DrawForStick`, cubic; LT/RT scale the range continuously ×0.1…×10), so Phase 3 is done
apart from editing outside the popover.
Second round: X opens the browser on release; while X is held the d-pad picks the side (Above/Left = before the
input, Below/Right = after the output, snapped inputs follow their snap) with `GamepadInsertPreview` marking it.
Right stick up/down changes the slider range in decades (`DrawForStick` keeps it until restart). Right stick
pressed + tilt moves the selected op's snapped group (`GamepadGroupDrag`, one undo step; no unsnap/splice/snap yet).
Third round: panning 375 px/s; browser result list moves on press with repeat and scrolls the selection into
view (keyboard too); no axis lock on the slider; popover reads A (apply, toggles bools), B (apply and close), X
(insert into the input, like Shift+I) itself; `HandleStickEditing` puts Vector2/3 on the sticks (right x/y,
left-y z) and colors (right: hue/saturation, left: brightness/alpha) without a component selector.
Later rounds: LB/RB undo/redo in the graph; X also accepts in the browser; Y in the browser opens the highlighted
operator's presets (B back to the list); Y in the popover resets to default; inserting after the shown op of an
unconnected chain end moves the output/background to the new op (all inserts, not only the pad).
Camera: View toggles a Camera context (B or View returns, toast); `Camera/GamepadCamera` is an
`ICameraManipulator` next to the SpaceMouse (left stick fly, right stick look, RT/LT lift, speed scales with the
view distance and CameraSpeed). Focus mode moved from View to Share (`GamePadState.Share`, SDL MISC1). The
background only takes camera input while a camera op is selected.
Open cleanup: Y means "forward" (presets) in the browser but "revert" (reset) in the popover; find one meaning.
Open ideas: a gain/bias control UI on the sticks, a visual color picker while editing colors, group drag with
unsnap/snap like the mouse.
Test set: `.tests-manual/gamepad-editor-control.md`. Reference controller: Xbox.

Much of what this needs already exists for keyboard control (`Plan_KeyboardControl.md`): arrow-key selection
navigation, the parameter popover, Tab insert, operator bookmarks, action toasts. **The gamepad is another
input source for those features, not a parallel implementation.** New work: the SDL gamepad service, pad
ownership, a focus model for the symbol browser's namespace drill, analog value editing and camera control.

## Decisions

- **Ownership via a status indicator, not a modifier button.** A menu-bar indicator next to the debug server
  indicator (`AppMenuBar.DrawDebugServerIndicator`) appears only while a controller is connected and flashes
  when input arrives. Clicking it cycles **Ops only → UI → Off**:
  - *Ops only*: `[Gamepad]` reads the pad, the editor ignores it.
  - *UI*: the editor reads the pad, and `[Gamepad]` sees a neutral pad, so panning the graph doesn't also
    steer the scene.
  - *Off*: neither reads it.
  The state persists in `UserSettings` (a restart mid-show must not reset it) and shows a toast on change.
- **Own focus model, not ImGui nav.** ImGui's focus handling has cost too much time before, and its
  directional navigation across wrapped multi-column layouts is unpredictable. Focusable entries register
  their rect while drawing; a shared navigator picks the target, draws the focus frame and scrolls it into view.
- **SDL3 gamepad API replaces XInput.** It works on Windows and Linux, supports hot-plugging and maps
  PlayStation and Switch pads to the same layout. One service feeds the editor and the `[Gamepad]` op.
- **Suggestions first.** About 90 % of insertions are picks from the browser's type-filtered suggestion list,
  which already works with ↑↓ / Return. The namespace drill matters less often but is the hardest part, so it
  is spiked first.
- **No mode indicator beyond toasts.**

## Conventions

Xbox defaults, consistent with the projector calibration on `main` (`SetupOutputView.PinGamepad.cs`):

- **A** confirms or enters. **B** goes back, cancels or closes.
- **Left stick** navigates: pans the graph, flies the camera.
- **Right stick** changes the active thing: a value, a calibration point, the camera's look direction.
  Response is cubic in deflection (as in calibration): a light touch for fine steps, full travel for speed.
- **D-pad** makes discrete choices: nearest item in that direction, auto-repeat while held (~400 ms delay,
  then ~100 ms).
- **LB / RB** step through lists or pages. **LT / RT** are analog modifiers.
- Stick clicks are rare toggles. Anything destructive needs a long press.

## Button map

### Graph (default context)

| Input | Action | Reuses |
|---|---|---|
| Left stick | Pan | — |
| LT / RT | Zoom out / in | — |
| D-pad | Select the nearest op in that direction | `SelectToAbove/Right/Below/Left`, `SelectionNavigation` |
| A | Edit parameters of the selected op; without a selection insert in the view centre | `EditParameter` → `ParameterPopover` |
| B | Deselect; with nothing selected, go up to the parent composition | — |
| X | Insert after the selected op (or in the view centre); opens the browser type-filtered | Tab insert |
| Hold X + d-pad, release X | Insert on the picked side, previewed while X is held | Tab / Shift+Tab / Ctrl+Tab insert |
| Y | Show the selection in the output; hold to only peek (camera switching moves elsewhere) | `PinToOutputWindow` → `OutputPeek` |
| LB / RB | Undo / redo (bookmark stepping stays bindable on the keyboard) | `Undo` / `Redo` |
| Menu (☰) | Play / pause | — |
| View (⧉) | Steer the camera (View or B returns) | `GamepadCamera` |
| Share | Toggle focus mode | `ToggleFocusMode` |

Insert directions follow MagGraph's flow: right and down add after the selected op's output (Shift+Tab and Tab);
left and up add before its input (new). Panning stops while X is held, and an arrow previews the direction.

### Parameter editing (after A)

- D-pad ↑↓ moves the popover cursor.
- Right stick changes the value: X axis for floats, X and Y for Vector2 parameters. Generalise
  `InfinitySliderOverlay.DrawForKeyboard` from a ±1 direction to an analog value.
- LT narrows ×0.1 and RT widens ×10, replacing Shift / Ctrl for this input path.
- One undo step per stick gesture (from leaving rest to returning to rest).
- Long-press right-stick click resets to the default.
- B closes the popover.

### Symbol browser

- Suggestion list: D-pad ↑↓ moves through it as Cursor Up / Down do now, A picks it (Return).
- Namespace drill: D-pad moves focus between entries; A opens the namespace or picks the symbol; B goes up one
  level, and closes the browser at the root.
- LB / RB jump between top-level pages (numbers, point, image, …).
- Left stick scrolls.
- Focus starts at the top-left entry. Revisit after trying it.

### Camera (after Y)

- Left stick flies, right stick looks or orbits, LT / RT move down / up.
- Long-press stick click resets the view.
- B or Y returns to the graph.

## Architecture

New namespace `Editor/Gui/Interaction/Gamepad/`:

- **`GamepadInput`**: opens the first connected pad through SDL3 (`SDL_INIT_GAMEPAD` next to the existing
  `SDL_INIT_VIDEO` in `Program.cs`) and handles hot-plugging. Once per frame it polls a state struct (no
  allocations) with pressed / released edges, D-pad auto-repeat, stick deadzone and cubic response. Exposes
  `LastActivityTicksMs` for the indicator flash.
- **Ownership**: an enum in `UserSettings.Config` and an indicator drawn in `AppMenuBar`. In *UI* mode the
  `[Gamepad]` op's source returns a neutral state.
- **Context claiming**: like `KeyActionHandling.ClaimArrowKeys`, the active context claims the pad each frame,
  and the claim from the previous frame wins so draw order doesn't matter. Priority:
  calibration → symbol browser → parameter popover → camera → graph.
- **`FocusNavigator`**: a reusable list of `(rect, entryId)` filled while drawing, reset each frame. Direction
  ranking matches `SelectionNavigation` (gap along the direction, sideways penalty). It draws the focus frame
  (whole pixels, `SetupStrokes` approach) and scrolls the target into view. Keyboard arrows drive it too, which
  also gives the drill keyboard navigation.
- **`[Gamepad]` op**: `IoServices/GamePadInput.cs` keeps its `GamePadState` struct, but the state comes from a
  source the editor registers (SDL) instead of `XInputGamepad` directly.
- **Camera**: `CameraInteraction` takes mouse deltas and WASD (`ClaimCameraKeys`). It gets an analog input
  path, ideally matching `[ActionCamera]`'s handling of the pad so flying feels the same in both places.

Notes:
- `SymbolBrowsing.Draw` builds lists and runs LINQ every frame (`new List`, `OrderByDescending().ToList()`).
  Fix that while adding the focus model, since the browser stays open while it is navigated.
- Calibration on `main` polls SharpDX XInput directly with its own `_lastPad` / `WasPressed`. When it is merged
  into this branch, port it onto `GamepadInput` and register it as the top context.

## Phases (each shippable)

**Phase 0: focus model spike (keyboard only).** `FocusNavigator` in the symbol browser's namespace drill,
driven by arrow keys, Return and Backspace/Esc. Validates the hardest part with no gamepad code.

**Phase 1: SDL gamepad service and ownership.** `GamepadInput`, the indicator, the ownership setting, and the
`[Gamepad]` op switched to the shared source. Check `[Gamepad]` and `GamePadExample` still work on Windows and
now work on Linux.

**Phase 2: discrete mapping.** D-pad selection, A / B, Menu, View, LB / RB bookmarks, suggestion list picking,
the drill on the D-pad, page jumps on the shoulder buttons. Toasts for actions without visible feedback.

**Phase 3: analog.** Pan and zoom, and right-stick value editing in the parameter popover.

**Phase 4: insert gesture.** Hold X + flick, with direction preview; "insert before" for left and up.

**Phase 5: camera context.** Y toggle and the analog path into `CameraInteraction`.

**Phase 6: calibration port** once it has been merged from `main`.

Each phase adds or updates a manual test set in `.tests-manual/` and the `.help/` page for gamepad control.

## Open questions

1. Focus start position in the drill: top left for now, maybe the last pick on that page.
2. B at the drill root: close, or nothing? Decide by trying.
3. Player: does it initialise SDL? If not, the `[Gamepad]` op keeps XInput in the Player on Windows.
4. Background input: SDL delivers gamepad events only to the focused window unless
   `SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS` is set. Set it, so the pad works while another app has focus?
5. Rebinding: a fixed map first. Add gamepad buttons to `KeyMap` later?
6. Several connected pads: first one only, or pick one in the indicator's popup?
7. Toast labels for buttons: Xbox glyphs only, or follow the pad type SDL reports (PlayStation shapes)?
