# Game controller support

Game controllers through SDL3 for operators and for steering the camera. Graph editing from a controller was
explored and parked; its learnings live in `Plan_GamepadEditing.md` on branch `feat/gamepad-control`.

**Status (2026-11-05):**
- Done: `IoServices/SdlGamepads` reads pads through SDL3 (Editor and Player initialise it after `SDL_Init`,
  background events on, radial dead zones, 0.5 s hot-plug scan). `[Gamepad]` reads it instead of XInput, so it
  works on Linux. `Gamepad/GamepadIndicator` in the menu bar appears with a controller, flashes on input, and
  cycles `UserSettings.GamepadUsage`: Operators → Editor (steers the camera) → Off. In Editor mode `[Gamepad]` sees
  a released pad.
- Done, needs testing with a pad: `Camera/GamepadCamera`, an `ICameraManipulator` next to the SpaceMouse. Left
  stick flies, right stick looks around, triggers lift and lower; speed follows the target distance and
  `CameraSpeed`; pitch stops short of straight up/down.
- `XInputGamepad` and the SharpDX.XInput package stay until main's projector calibration (which polls XInput) is
  ported onto `SdlGamepads`.
- Settings → Input Devices (formerly Space Mouse) has a Game Controller group (status of connected controllers,
  Rescan, usage, camera smoothing in seconds, move/look speed, invert look) and a SpaceMouse group (status from the
  last motion, since raw input reports no connection; Windows only).
- Test sets: `.tests-manual/gamepad-usage-indicator.md`.

## Next

1. Test the camera steering with a pad, tune speeds.
2. Small camera additions: stick click to reset the view, shoulder buttons to change `CameraSpeed` live.
3. Port the calibration on `main` from XInput to `SdlGamepads`, then drop `XInputGamepad` and SharpDX.XInput.
4. SpaceMouse on Linux (today Win32 raw input only), next to `GamepadCamera`.
5. Follow the camera overhaul (`Plan_CameraOverhaul.md`): the pad should fly the new camera model, including the
   Godot-style pitch stop with roll.

Hardware note: Xbox Series controllers over Bluetooth may stay silent on Linux with the stock `hid_microsoft`
driver; test over USB or with `xpadneo`.
