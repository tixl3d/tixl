# Camera overhaul (orientation, projection blend, navigation)

Bring TiXL's cameras closer to Blender, Godot and Unreal: one camera model that can be driven by position/target
or by rotation (euler or quaternion), blends continuously between perspective and orthographic, never flips upside
down, and navigates like Unreal (WASD only while flying). Input devices (mouse, keyboard, SpaceMouse, game
controller) share one set of camera operations.

**Status (2026-11-05):** research only, nothing changed. Undecided whether it lands in 4.4 or 4.5; the phasing
below keeps the editor part free of file-format changes so it can go first.

## Goals

1. **No gimbal lock, no upside down.** Pitch stops exactly at straight up/down (as in Godot); roll is a separate,
   finely controllable axis.
2. **Position/target or rotation.** A camera can be defined by a target (look-at) or by euler/quaternion rotation,
   and switching keeps the view.
3. **Seamless perspective ↔ orthographic blend.** The framing at the focus distance stays put while blending.
4. **WASD only while flying.** Hold the right mouse button to fly (mouse looks around the camera position, WASD/QE
   move, wheel sets speed); otherwise WASD stays free for the graph and other shortcuts.
5. Devices plug in through `ICameraManipulator` and no longer each implement their own pole clamp.

## Current state

**Definition**
- `ICamera` (`Core/Operator/Interfaces/ICamera.cs`) exposes `CameraPosition`, `CameraTarget`, `CameraRoll` and a
  `CameraDefinition` (Position, Target, Up, Roll in degrees, FOV, NearFar, LensShift, Position/RotationOffset). The
  view is `LookAtRH(eye, Target, Up)` × roll × rotation offset. `GraphicsMath.LookAtRH` degenerates when the view
  direction is parallel to Up.
- Operators in `Operators/Lib/Symbols/render/camera/`:
  - `[Camera]` has position/target/roll/up inputs and writes back through `Animator.Update*InputValue`, which keys
    animated inputs at the current time.
  - `[OrthographicCamera]` is a separate op; its `CameraDefinition` is `new()` ("Not implemented"), so it can't
    be blended.
  - `[CameraWithRotation]` already offers Euler/Quaternion modes. Reportedly it passes roll in radians where
    degrees are expected (not verified).
  - `[OrbitCamera]` and `[ActionCamera]` keep their own state, without a pole clamp.
  - `[BlendCameras]` slerps orientations taken from the matrices. Reportedly it applies the rotation offset twice
    (not verified).
- `Core/Output/Pose` (position + quaternion) and `Core/Output/Projection` (perspective/orthographic kind,
  `OrthographicHeight`) already have the target shape, built for output setup. `Projection` stores lens shift with
  the opposite sign to `CameraDefinition`.

**Editor control** (`Editor/Gui/Interaction/Camera/`)
- `CameraSetup` is position + target; `CameraInteraction` keeps an intended and a smoothed copy.
- Mouse: left drag orbits, Alt+left pans, Ctrl+left or middle looks around, right drag pans, wheel dollies.
- Orbit and look-around rotate about the left axis without a clamp, so crossing a pole flips the view.
  `ViewAxis.ComputeForCamera` passes `CameraRoll` (degrees on `[Camera]`) as radians.
- WASD (`ManipulateCameraByKeyboard`) is live whenever the output window is hovered and focused, and claims the
  camera keys (`KeyActionHandling.ClaimCameraKeys`).
- Devices (`ICameraManipulator`: SpaceMouse, `GamepadCamera`) each clamp at `viewDir.Y > 0.95` themselves and
  ignore roll; the SpaceMouse's roll axis is unused.
- The viewer camera (`EvaluationContext.SetViewFromCamera`) hard-codes FOV 45° and Up = Y and ignores roll.
- In focus mode the graph background runs its own `CameraInteraction` on the selected camera next to the output
  window's. Possibly both move it in one frame (not verified).

**Stored data**
- `.t3`: camera inputs by Guid; animated vectors are three separate float curves.
- Output window state (`OutputWindowState`, in `.t3ui`): CameraPosition, CameraTarget, CameraRoll, CameraSpeed,
  PickedCameraId.

## Design

**One camera model:** pivot, orientation (quaternion), distance, vertical FOV, ortho blend (0 = perspective,
1 = orthographic), near/far, lens shift. Position = pivot − forward × distance. This matches `Core/Output/Pose` +
`Projection` and becomes the shape of `CameraSetup` (editor) and later `CameraDefinition` (operators).

**Mappings**
- From position/target/roll: orientation = LookRotation(target − position, world up) × roll about forward;
  distance = |target − position|; pivot = target.
- From euler (YXZ, degrees, as Godot and Unreal): orientation = yaw × pitch × roll; pivot = position + forward ×
  focus distance.
- Back to position/target for today's ops: target = pivot. Yaw is undefined at exactly ±90° pitch, so that
  direction stops just short (about 89.95°); euler and quaternion definitions reach 90° exactly.

**Godot-style pitch stop:** during interaction `CameraSetup` keeps accumulated yaw, pitch and roll and rebuilds the
orientation each frame. Pitch is clamped to ±90°; roll is its own axis and never changes yaw or pitch.
`StopAndSyncTo` decomposes the orientation, with an explicit branch at the poles. Degrees everywhere.

**Shared camera operations:** `CameraSetup.Rotate(yaw, pitch, roll)`, `MoveLocal(offset)`, `Orbit(yaw, pitch)`,
`Dolly(amount)`, `Pan(offset)`. Mouse, keyboard and all `ICameraManipulator` devices use them, so the clamp lives in
one place. The 0.95 hacks in `SpaceMouse` and `GamepadCamera` go, and the SpaceMouse's roll axis becomes roll.

**Fly mode (Unreal):** holding the right mouse button over the output flies. The mouse looks around the camera's
position, W/A/S/D/Q/E move, and the wheel changes speed (the speed logic exists in `HandleMouseWheel`). Camera keys
are claimed only while flying. Right-drag panning moves to middle drag (Alt+left keeps panning). A temporary setting
"WASD while hovering" eases the transition.

**Perspective ↔ orthographic blend:** orthographic height = 2 × distance × tan(fov / 2), so the pivot plane keeps
its framing. The projection blends its w row from −z to the constant distance; x/y scale matches at the pivot
depth, so the blend is continuous without dollying to infinity. In orthographic the wheel changes distance (which
is height).

## Phases

**Phase 1 (4.4, editor only, no format change)**
- New `CameraSetup` math with pivot/orientation, the pitch stop with roll, and the shared operations.
- Fly mode, with the transition setting.
- Devices on the shared operations; drop their own clamps.
- Fix roll units in `ViewAxis` and roll/FOV in `SetViewFromCamera`.
- Write back as position/target/roll, as today.
- Optional: an ortho toggle for the viewer camera only, stored additively in `OutputWindowState` (older files fall
  back to position/target).

**Phase 2 (4.5, operators and data)**
- A new `[Camera]` version through `Plan_OperatorVersioning` (`Supersedes`, same input Guids), adding RotationMode
  (LookAt/Euler/Quaternion), Rotation, OrthoBlend and FocusDistance.
- `CameraDefinition` gains orientation and projection blend; `[BlendCameras]` blends projection and applies the
  rotation offset once.
- Retire `[OrthographicCamera]` and `[CameraWithRotation]` behind the new `[Camera]`.

**Phase 3 (4.5, rendering)**
- Shaders take the eye position from the `CameraToWorld` origin (14 found, e.g. `mesh-Draw.hlsl`), which gives
  wrong view vectors and specular highlights in ortho. They need a view direction from the camera buffer.
- Gizmo scale assumes 45° and uses the distance to the camera (`TransformGizmoHandling`); it needs to handle ortho.
- Picking unprojects near/far and is safe. `[ReuseCamera]` and `[CamPosition]` only read matrices.

## Risks and constraints

- **Don't animate quaternions per component:** the curves are separate floats and won't slerp. Animated cameras
  keep euler angles or position/target as their animated inputs.
- Writing into an animated camera keys it at the current time. The overhaul must keep the guard that stops
  smoothing when time moves. There seems to be no undo for camera writes today (not verified).
- TAA relies on a single consumer of `_TransformsCBufferFromCam` per pass; a changed camera buffer layout must keep
  that.
- The Player has no camera interaction and only evaluates operator matrices, so the risk is low.

## Open questions

1. 4.4 or 4.5 for Phase 1? It is self-contained, but changes mouse habits (right drag).
2. Keep right-drag pan as an option for users who rely on it?
3. Should the viewer camera get the ortho blend in Phase 1, or wait for the operator model?
4. Euler order and units: YXZ in degrees (Godot/Unreal) assumed.
