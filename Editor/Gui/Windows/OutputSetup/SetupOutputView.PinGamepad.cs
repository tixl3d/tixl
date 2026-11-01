#nullable enable
using ImGuiNET;
using SharpDX.XInput;
using T3.Core.Output;
using T3.IoServices;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// "Align Points" from a game controller, so the person aligning can stand at the wall: the right stick moves the
/// active point, the d-pad goes to the nearest point in its direction, the shoulder buttons cycle through all of
/// them, the right stick's button releases the active one. The left stick defines which way is
/// up on the wall, which a projector mounted at an angle makes different from up on its canvas; its button sets
/// that direction, or resets the alignment while the stick is centred.
/// </summary>
internal sealed partial class SetupOutputView
{
    private void UpdatePointsFromGamepad(Setup setup, Surface surface, Surface.OutputMapping mapping, Vector2 canvasSize)
    {
        if (!TryPollGamepad(out var pad))
        {
            FinishGamepadMove(setup);
            return;
        }

        var wallRightOnCanvas = new Vector2(-_wallUpOnCanvas.Y, _wallUpOnCanvas.X);
        if (WasPressed(pad.LeftShoulder, _lastPad.LeftShoulder))
        {
            CycleActivePoint(surface, -1);
        }
        else if (WasPressed(pad.RightShoulder, _lastPad.RightShoulder) || !TryFindPoint(surface, _activePointId, out _))
        {
            CycleActivePoint(surface, 1);
        }
        else if (WasPressed(pad.DirectionalPad.Left, _lastPad.DirectionalPad.Left))
        {
            SelectPointInDirection(surface, mapping, canvasSize, -wallRightOnCanvas);
        }
        else if (WasPressed(pad.DirectionalPad.Right, _lastPad.DirectionalPad.Right))
        {
            SelectPointInDirection(surface, mapping, canvasSize, wallRightOnCanvas);
        }
        else if (WasPressed(pad.DirectionalPad.Up, _lastPad.DirectionalPad.Up))
        {
            SelectPointInDirection(surface, mapping, canvasSize, _wallUpOnCanvas);
        }
        else if (WasPressed(pad.DirectionalPad.Down, _lastPad.DirectionalPad.Down))
        {
            SelectPointInDirection(surface, mapping, canvasSize, -_wallUpOnCanvas);
        }

        var hasDirection = pad.LeftThumb.Length() > MinThumbForDirection;

        // The stick's up is the canvas' up, so the arrow is off on the wall by however the projector is mounted.
        var thumbDirection = hasDirection ? Vector2.Normalize(new Vector2(pad.LeftThumb.X, -pad.LeftThumb.Y)) : Vector2.Zero;
        if (hasDirection)
            CalibrationOverlay.SetUpDirectionPreview(surface.Id, thumbDirection);

        if (WasPressed(pad.LeftStickButton, _lastPad.LeftStickButton))
        {
            if (hasDirection)
            {
                _wallUpOnCanvas = thumbDirection;
            }
            else
            {
                FinishGamepadMove(setup);
                ResetAlignment(setup, mapping);
            }
        }

        if (WasPressed(pad.RightStickButton, _lastPad.RightStickButton)
            && mapping.PointAims.TryGetValue(_activePointId, out var activeAim) && activeAim.IsAimed)
        {
            FinishGamepadMove(setup);
            ReleasePoint(setup, mapping, _activePointId);
        }

        _lastPad = pad;

        MoveActivePoint(setup, surface, mapping, canvasSize, pad.RightThumb);
    }

    private void MoveActivePoint(Setup setup, Surface surface, Surface.OutputMapping mapping, Vector2 canvasSize, Vector2 thumb)
    {
        var deflection = thumb.Length();
        if (deflection <= 0)
        {
            FinishGamepadMove(setup);
            return;
        }

        if (!TryFindPoint(surface, _activePointId, out var point)
            || !SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, canvasSize, out var surfaceToOutput))
            return;

        if (!_isGamepadMovingPoint)
        {
            // A mouse drag in progress keeps the gesture slot.
            if (_gesture.IsLive)
                return;

            BeginPointMove(setup, surface, mapping);
            _isGamepadMovingPoint = true;
        }

        var wallRightOnCanvas = new Vector2(-_wallUpOnCanvas.Y, _wallUpOnCanvas.X);
        var direction = _wallUpOnCanvas * thumb.Y + wallRightOnCanvas * thumb.X;

        // Speed grows with the cube of the deflection: full travel crosses the wall, a light touch sets single pixels.
        var pixels = direction * deflection * deflection * MaxPointSpeedOfCanvasHeightPerSec * canvasSize.Y * ImGui.GetIO().DeltaTime;
        var aim = AimOf(mapping, point!.Id, point.P1, surfaceToOutput, canvasSize);
        mapping.PointAims[point.Id] = new Surface.OutputMapping.PointAim(aim.Position + pixels / canvasSize, true);
        SolvePinFromTargets(surface, mapping, canvasSize);
    }

    /** Ends the undo step of a stick move, once the stick is back at rest or the tool went away. */
    private void FinishGamepadMove(Setup setup)
    {
        if (!_isGamepadMovingPoint)
            return;

        _isGamepadMovingPoint = false;
        EndGesture(setup);
    }

    /** Walks the annotations from the active point to the next point in that direction, wrapping around. */
    private void CycleActivePoint(Surface surface, int step)
    {
        var count = surface.Annotations.Count;
        var activeIndex = surface.Annotations.FindIndex(annotation => annotation.Id == _activePointId);
        for (var offset = 1; offset <= count; offset++)
        {
            var candidate = surface.Annotations[((activeIndex + step * offset) % count + count) % count];
            if (candidate.IsPoint)
            {
                _activePointId = candidate.Id;
                return;
            }
        }
    }

    /// <summary>
    /// Goes to the closest point lying in a direction from the active one, as seen on the wall. Sideways distance
    /// counts double, so the press favours what is straight ahead. Stays put with nothing in that direction.
    /// </summary>
    private void SelectPointInDirection(Surface surface, Surface.OutputMapping mapping, Vector2 canvasSize, Vector2 direction)
    {
        if (!TryFindPoint(surface, _activePointId, out var active)
            || !SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, canvasSize, out var surfaceToOutput))
            return;

        var activePosition = AimOf(mapping, active!.Id, active.P1, surfaceToOutput, canvasSize).Position * canvasSize;
        var sideways = new Vector2(-direction.Y, direction.X);
        var bestCost = float.MaxValue;
        foreach (var candidate in surface.Annotations)
        {
            if (!candidate.IsPoint || candidate.Id == _activePointId)
                continue;

            var offset = AimOf(mapping, candidate.Id, candidate.P1, surfaceToOutput, canvasSize).Position * canvasSize - activePosition;
            var ahead = Vector2.Dot(offset, direction);
            if (ahead <= 0)
                continue;

            var cost = ahead + 2 * MathF.Abs(Vector2.Dot(offset, sideways));
            if (cost < bestCost)
            {
                bestCost = cost;
                _activePointId = candidate.Id;
            }
        }
    }

    private static bool TryFindPoint(Surface surface, Guid pointId, out Annotation? point)
    {
        foreach (var annotation in surface.Annotations)
        {
            if (annotation.IsPoint && annotation.Id == pointId)
            {
                point = annotation;
                return true;
            }
        }

        point = null;
        return false;
    }

    /** XInput stalls for milliseconds when asked about an empty slot, so a missing controller is only looked for now and then. */
    private bool TryPollGamepad(out GamePadState pad)
    {
        pad = default;
        if (_gamepad == null)
        {
            var now = ImGui.GetTime();
            if (now < _nextGamepadSearchTime)
                return false;

            _nextGamepadSearchTime = now + GamepadSearchIntervalSec;
            foreach (var userIndex in _gamepadSlots)
            {
                var controller = new Controller(userIndex);
                if (controller.IsConnected)
                {
                    _gamepad = controller;
                    break;
                }
            }

            if (_gamepad == null)
                return false;
        }

        if (!_gamepad.GetState(out var state))
        {
            _gamepad = null;
            return false;
        }

        pad = XInputGamepad.Create(state.Gamepad);
        return true;
    }

    private static bool WasPressed(bool isDown, bool wasDown) => isDown && !wasDown;

    private const float MinThumbForDirection = 0.3f;
    private const float MaxPointSpeedOfCanvasHeightPerSec = 0.25f;
    private const double GamepadSearchIntervalSec = 1;
    private static readonly UserIndex[] _gamepadSlots = [UserIndex.One, UserIndex.Two, UserIndex.Three, UserIndex.Four];

    private Controller? _gamepad;
    private double _nextGamepadSearchTime;
    private GamePadState _lastPad;
    private bool _isGamepadMovingPoint;

    /** Which way pushing the right stick up moves a point, as a unit vector on the canvas. Starts as the canvas' own up. */
    private Vector2 _wallUpOnCanvas = new(0, -1);
}
