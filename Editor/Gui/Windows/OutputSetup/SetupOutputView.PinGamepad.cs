#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.IoServices;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// "Align Points" from a game controller, so the person aligning can stand at the wall: the right stick moves the
/// active point, the d-pad goes to the nearest point in its direction, the shoulder buttons cycle through all of
/// them (the four outermost first, see <see cref="WritePointCycleOrder"/>), the right stick's button releases the
/// active one. The left stick defines which way is
/// up on the wall, which a projector mounted at an angle makes different from up on its canvas; its button sets
/// that direction, or resets the alignment while the stick is centred.
/// </summary>
internal sealed partial class SetupOutputView
{
    private void UpdatePointsFromGamepad(Setup setup, AlignSubject subject, Surface.OutputMapping mapping, Vector2 canvasSize)
    {
        var traced = subject.Traced;
        if (!TryPollGamepad(out var pad))
        {
            FinishGamepadMove(setup);
            return;
        }

        var wallRightOnCanvas = new Vector2(-_wallUpOnCanvas.Y, _wallUpOnCanvas.X);
        if (WasPressed(pad.LeftShoulder, _lastPad.LeftShoulder))
        {
            CycleActivePoint(subject, -1);
        }
        else if (WasPressed(pad.RightShoulder, _lastPad.RightShoulder) || !TryFindPoint(traced, _activePointId, out _))
        {
            CycleActivePoint(subject, 1);
        }
        else if (WasPressed(pad.DirectionalPad.Left, _lastPad.DirectionalPad.Left))
        {
            SelectPointInDirection(subject, mapping, canvasSize, -wallRightOnCanvas);
        }
        else if (WasPressed(pad.DirectionalPad.Right, _lastPad.DirectionalPad.Right))
        {
            SelectPointInDirection(subject, mapping, canvasSize, wallRightOnCanvas);
        }
        else if (WasPressed(pad.DirectionalPad.Up, _lastPad.DirectionalPad.Up))
        {
            SelectPointInDirection(subject, mapping, canvasSize, _wallUpOnCanvas);
        }
        else if (WasPressed(pad.DirectionalPad.Down, _lastPad.DirectionalPad.Down))
        {
            SelectPointInDirection(subject, mapping, canvasSize, -_wallUpOnCanvas);
        }

        var hasDirection = pad.LeftThumb.Length() > MinThumbForDirection;

        // The stick's up is the canvas' up, so the arrow is off on the wall by however the projector is mounted.
        var thumbDirection = hasDirection ? Vector2.Normalize(new Vector2(pad.LeftThumb.X, -pad.LeftThumb.Y)) : Vector2.Zero;
        if (hasDirection)
            CalibrationOverlay.SetUpDirectionPreview(subject.Pinned.Id, thumbDirection);

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

        MoveActivePoint(setup, subject, mapping, canvasSize, pad.RightThumb);
    }

    private void MoveActivePoint(Setup setup, AlignSubject subject, Surface.OutputMapping mapping, Vector2 canvasSize, Vector2 thumb)
    {
        var deflection = thumb.Length();
        if (deflection <= 0)
        {
            FinishGamepadMove(setup);
            return;
        }

        if (!TryFindPoint(subject.Traced, _activePointId, out var point)
            || !SurfaceGeometry.TryGetSurfaceToOutput(subject.Pinned, mapping, canvasSize, out var surfaceToOutput))
            return;

        if (!_isGamepadMovingPoint)
        {
            // A mouse drag in progress keeps the gesture slot.
            if (_gesture.IsLive)
                return;

            BeginPointMove(setup, subject, mapping);
            _isGamepadMovingPoint = true;
        }

        var wallRightOnCanvas = new Vector2(-_wallUpOnCanvas.Y, _wallUpOnCanvas.X);
        var direction = _wallUpOnCanvas * thumb.Y + wallRightOnCanvas * thumb.X;

        var speed = PointSpeedPxPerSec(deflection);
        var pixels = direction / deflection * speed * ImGui.GetIO().DeltaTime;
        var aim = AimOf(mapping, point!.Id, point.P1 + subject.TracedToPinned, surfaceToOutput, canvasSize);
        mapping.PointAims[point.Id] = new Surface.OutputMapping.PointAim(aim.Position + pixels / canvasSize, true);
        SolvePinFromTargets(subject, mapping, canvasSize);
    }

    /** Ends the undo step of a stick move, once the stick is back at rest or the tool went away. */
    private void FinishGamepadMove(Setup setup)
    {
        if (!_isGamepadMovingPoint)
            return;

        _isGamepadMovingPoint = false;
        EndGesture(setup);
    }

    /** Steps the active point along the shoulder-button order, wrapping around; with none active, starts at an end. */
    private void CycleActivePoint(AlignSubject subject, int step)
    {
        WritePointCycleOrder(subject.Traced, subject.TracedToPinned);
        var count = _cycleOrder.Count;
        if (count == 0)
            return;

        var activeIndex = _cycleOrder.IndexOf(_activePointId);
        var nextIndex = activeIndex < 0
                            ? step > 0 ? 0 : count - 1
                            : ((activeIndex + step) % count + count) % count;
        _activePointId = _cycleOrder[nextIndex];
    }

    /// <summary>
    /// Goes to the closest point lying in a direction from the active one, as seen on the wall. Sideways distance
    /// counts double, so the press favours what is straight ahead. Stays put with nothing in that direction.
    /// </summary>
    private void SelectPointInDirection(AlignSubject subject, Surface.OutputMapping mapping, Vector2 canvasSize, Vector2 direction)
    {
        if (!TryFindPoint(subject.Traced, _activePointId, out var active)
            || !SurfaceGeometry.TryGetSurfaceToOutput(subject.Pinned, mapping, canvasSize, out var surfaceToOutput))
            return;

        var activePosition = AimOf(mapping, active!.Id, active.P1 + subject.TracedToPinned, surfaceToOutput, canvasSize).Position * canvasSize;
        var sideways = new Vector2(-direction.Y, direction.X);
        var bestCost = float.MaxValue;
        foreach (var candidate in subject.Traced.Annotations)
        {
            if (!candidate.IsPoint || candidate.Id == _activePointId)
                continue;

            var offset = AimOf(mapping, candidate.Id, candidate.P1 + subject.TracedToPinned, surfaceToOutput, canvasSize).Position * canvasSize - activePosition;
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

    /** Reads the first controller through SDL, which works on every platform (XInput only exists on Windows). */
    private static bool TryPollGamepad(out GamePadState pad)
    {
        return SdlGamepads.TryGetState(0, out pad);
    }

    private static bool WasPressed(bool isDown, bool wasDown) => isDown && !wasDown;

    private const float MinThumbForDirection = 0.3f;

    /// <summary>
    /// Point speed in projector pixels per second for a stick deflection past the dead zone, from a table: a crawl
    /// across the lower third for placing a point precisely, then fast enough to cross the wall. Between two speeds
    /// the curve runs on a log scale, so it accelerates evenly instead of jumping.
    /// </summary>
    private static float PointSpeedPxPerSec(float deflection)
    {
        deflection = Math.Clamp(deflection, 0, 1);
        for (var i = 1; i < _speedDeflections.Length; i++)
        {
            if (deflection > _speedDeflections[i])
                continue;

            var t = (deflection - _speedDeflections[i - 1]) / (_speedDeflections[i] - _speedDeflections[i - 1]);
            var from = _speedsPxPerSec[i - 1];
            var to = _speedsPxPerSec[i];
            return from > 0 ? from * MathF.Pow(to / from, t) : to * t;
        }

        return _speedsPxPerSec[^1];
    }

    /** Stick deflections and the point speeds they give. */
    private static readonly float[] _speedDeflections = [0f, 0.1f, 0.3f, 0.6f, 0.8f, 1f];

    private static readonly float[] _speedsPxPerSec = [0f, 1f, 2f, 50f, 100f, 400f];

    private GamePadState _lastPad;
    private bool _isGamepadMovingPoint;

    /** Which way pushing the right stick up moves a point, as a unit vector on the canvas. Starts as the canvas' own up. */
    private Vector2 _wallUpOnCanvas = new(0, -1);
}
