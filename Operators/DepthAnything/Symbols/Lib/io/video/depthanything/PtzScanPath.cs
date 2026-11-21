using System;
using System.Collections.Generic;
using T3.Core.Animation;
using T3.Core.Operator.Interfaces;

namespace Lib.io.video.depthanything;

/// <summary>
/// Drives a pan/tilt camera along a serpentine raster for room scanning. Continuous mode glides the target along the
/// path at SweepSpeed and holds while the camera lags more than MaxLead, with Scanning on for DepthScan3D's Capture;
/// stop mode moves, waits until the camera's reported position arrived and the depth settled, and pulses Capture.
/// Pan/Tilt are ONVIF generic-space values for <c>OnvifCamera</c>; CurrentPan/CurrentTilt (degrees) come from
/// <c>AxisPtzCamera</c>.
/// </summary>
[Guid("4b8e2d17-6a93-4f05-9c21-d7e3a5b4f690")]
public sealed class PtzScanPath : Instance<PtzScanPath>, IStatusProvider
{
    [Output(Guid = "5c9f3e28-7ba4-4016-8d32-e8f4b6c50701", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Pan = new();

    [Output(Guid = "6da04f39-8cb5-4127-9e43-f905c7d61812", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Tilt = new();

    [Output(Guid = "7eb1504a-9dc6-4238-8f54-0a16d8e72923", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> Move = new();

    [Output(Guid = "8fc2615b-aed7-4349-9065-1b27e9f83a34", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> Capture = new();

    [Output(Guid = "90d3726c-bfe8-445a-a176-2c38fa094b45", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Progress = new();

    [Output(Guid = "a1e4837d-c0f9-456b-b287-3d490b1a5c56", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> Done = new();

    [Output(Guid = "b2f5948e-d10a-467c-8398-4e5a1c2b6d67", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> StopIndex = new();

    /** On while a continuous sweep runs and the camera keeps up: connect to DepthScan3D.Capture. */
    [Output(Guid = "c3a6a59f-e21b-478d-84a9-5f6b2d3c7e89", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> Scanning = new();

    public PtzScanPath()
    {
        Pan.UpdateAction = Update;
        Tilt.UpdateAction = Update;
        Move.UpdateAction = Update;
        Capture.UpdateAction = Update;
        Progress.UpdateAction = Update;
        Done.UpdateAction = Update;
        StopIndex.UpdateAction = Update;
        Scanning.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        // Every output shares this update: stepping the sequence once per pull would end the one-frame Capture pulse
        // (and skip stops) before DepthScan3D reads it. The other pulls in the same frame keep this frame's values.
        if (_lastStepFrame == Playback.FrameCount)
            return;

        _lastStepFrame = Playback.FrameCount;

        var run = Run.GetValue(context);
        var restart = Restart.GetValue(context);
        var panStart = PanStart.GetValue(context);
        var panRange = Math.Clamp(PanRange.GetValue(context), 0f, 360f);
        var panStep = Math.Max(PanStep.GetValue(context), 1f);
        var tiltMin = TiltMin.GetValue(context);
        var tiltMax = TiltMax.GetValue(context);
        var tiltStep = Math.Max(TiltStep.GetValue(context), 1f);
        var poleTilt = PoleTilt.GetValue(context);
        var settleSec = Math.Max(SettleSec.GetValue(context), 0f);
        var arrivalTolerance = Math.Max(ArrivalTolerance.GetValue(context), 0.05f);
        var currentPan = CurrentPan.GetValue(context);
        var currentTilt = CurrentTilt.GetValue(context);
        var panDegreesPerUnit = PanDegreesPerUnit.GetValue(context);
        var tiltDegreesPerUnit = TiltDegreesPerUnit.GetValue(context);
        var tiltZeroAt = TiltZeroAt.GetValue(context);
        var continuous = Continuous.GetValue(context);
        var sweepSpeed = Math.Max(SweepSpeed.GetValue(context), 0.1f);
        var maxLead = Math.Max(MaxLead.GetValue(context), 0.5f);

        var layout = (panStart, panRange, panStep, tiltMin, tiltMax, tiltStep, poleTilt);
        if (!layout.Equals(_layout))
        {
            _layout = layout;
            BuildStops(panStart, panRange, panStep, tiltMin, tiltMax, tiltStep, poleTilt);
            ResetProgress();
        }

        if (restart && !_wasRestart)
            ResetProgress();

        _wasRestart = restart;

        var now = Playback.RunTimeInSecs;
        var frameSec = (float)Math.Clamp(now - _lastStepTime, 0, 0.1);
        _lastStepTime = now;
        var capture = false;
        var scanning = false;
        float panDegrees = 0, tiltDegrees = 0;
        if (continuous)
        {
            // The target glides along the stop polyline and waits while the camera trails, so every merged frame's
            // pose is one the camera actually reached
            var (cursorPan, cursorTilt) = PathPosition(_cursor);
            var lag = MathF.Max(AngleDistance(currentPan, cursorPan), MathF.Abs(currentTilt - cursorTilt));
            var finished = _stops.Count == 0 || _cursor >= _pathLength;
            if (run && !finished && lag <= maxLead)
                _cursor = MathF.Min(_cursor + sweepSpeed * frameSec, _pathLength);

            scanning = run && !finished && lag <= maxLead * 1.5f;
            (panDegrees, tiltDegrees) = PathPosition(_cursor);
            _stopIndex = finished ? _stops.Count : StopIndexAt(_cursor);
        }
        else if (run && _stops.Count > 0 && _stopIndex < _stops.Count)
        {
            var (targetPan, targetTilt) = _stops[_stopIndex];
            switch (_phase)
            {
                case Phase.Moving:
                    if (AngleDistance(currentPan, targetPan) <= arrivalTolerance && MathF.Abs(currentTilt - targetTilt) <= arrivalTolerance)
                    {
                        _phase = Phase.Settling;
                        _phaseStartTime = now;
                    }

                    break;

                case Phase.Settling:
                    // Depth smoothing and the video delay both trail the arrival
                    if (now - _phaseStartTime >= settleSec)
                    {
                        _phase = Phase.Capturing;
                        _phaseStartTime = now;
                    }

                    break;

                case Phase.Capturing:
                    // Scanning stays on for a short window of still frames that merge into one sharp capture
                    scanning = true;
                    if (now - _phaseStartTime >= CaptureWindowSec)
                    {
                        capture = true;
                        _phase = Phase.Captured;
                        _phaseStartTime = now;
                    }

                    break;

                case Phase.Captured:
                    // One frame of Capture is the pulse; the next stop starts on the following frame
                    _stopIndex++;
                    _phase = Phase.Moving;
                    break;
            }
        }

        if (!continuous)
        {
            var shownIndex = Math.Min(_stopIndex, Math.Max(_stops.Count - 1, 0));
            (panDegrees, tiltDegrees) = _stops.Count > 0 ? _stops[shownIndex] : (panStart, tiltMin);
        }

        // ONVIF generic space; pan wraps into -1..1
        var wrappedPan = (panDegrees + 540f) % 360f - 180f;
        Pan.Value = panDegreesPerUnit != 0 ? wrappedPan / panDegreesPerUnit : 0;
        Tilt.Value = tiltDegreesPerUnit != 0 ? tiltDegrees / tiltDegreesPerUnit + tiltZeroAt : 0;
        Move.Value = run && _stopIndex < _stops.Count;
        Capture.Value = capture;
        Scanning.Value = scanning;
        StopIndex.Value = _stopIndex;
        Done.Value = _stops.Count > 0 && _stopIndex >= _stops.Count;
        Progress.Value = continuous
                             ? (_pathLength > 0 ? _cursor / _pathLength : 0)
                             : _stops.Count > 0 ? (float)_stopIndex / _stops.Count : 0;
        _status = Done.Value
                      ? $"Done: {_stops.Count} captures"
                      : run
                          ? continuous
                                ? $"Sweep {Progress.Value * 100:F0}% pan {panDegrees:F0} tilt {tiltDegrees:F0}{(scanning ? "" : " (waiting for camera)")}"
                                : $"Stop {_stopIndex + 1}/{_stops.Count} pan {panDegrees:F0} tilt {tiltDegrees:F0} ({_phase})"
                          : $"Ready: {_stops.Count} stops";
    }

    /**
     * Serpentine raster: rows of constant tilt, alternating pan direction so the camera never swings back across the room.
     * Columns follow the view circle's size: a row at angle a from <paramref name="poleTilt"/> needs about sin(a) of the
     * horizon's stops, and the pole itself one.
     */
    private void BuildStops(float panStart, float panRange, float panStep, float tiltMin, float tiltMax, float tiltStep, float poleTilt)
    {
        _stops.Clear();
        var low = MathF.Min(tiltMin, tiltMax);
        var high = MathF.Max(tiltMin, tiltMax);
        var rows = Math.Max(1, (int)MathF.Ceiling((high - low) / tiltStep) + 1);
        var fullCircle = panRange >= 359.9f;
        for (var row = 0; row < rows; row++)
        {
            var tilt = rows == 1 ? low : low + (high - low) * row / (rows - 1);
            var fromPole = MathF.Abs(tilt - poleTilt) * MathF.PI / 180f;
            var circle = MathF.Abs(MathF.Sin(fromPole));
            var columns = Math.Max(1, (int)MathF.Ceiling(panRange * circle / panStep) + (fullCircle ? 0 : 1));
            for (var column = 0; column < columns; column++)
            {
                var index = row % 2 == 0 ? column : columns - 1 - column;
                var f = columns == 1 ? 0.5f : fullCircle ? (float)index / columns : (float)index / (columns - 1);
                _stops.Add((panStart + panRange * f, tilt));
            }
        }

        // Segment lengths in degrees of the larger axis, which bounds the camera's travel time
        _segmentLengths.Clear();
        _segmentLengths.Add(0);
        _pathLength = 0;
        for (var index = 1; index < _stops.Count; index++)
        {
            var length = MathF.Max(MathF.Abs(_stops[index].Pan - _stops[index - 1].Pan), MathF.Abs(_stops[index].Tilt - _stops[index - 1].Tilt));
            _segmentLengths.Add(length);
            _pathLength += length;
        }
    }

    private void ResetProgress()
    {
        _stopIndex = 0;
        _phase = Phase.Moving;
        _cursor = 0;
    }

    /** Pan/tilt at <paramref name="distance"/> degrees along the stop polyline (pan stays unwrapped between stops). */
    private (float Pan, float Tilt) PathPosition(float distance)
    {
        if (_stops.Count < 2)
            return _stops.Count == 1 ? _stops[0] : (0, 0);

        for (var index = 1; index < _stops.Count; index++)
        {
            var segment = _segmentLengths[index];
            if (distance <= segment || index == _stops.Count - 1)
            {
                var f = segment > 0 ? Math.Clamp(distance / segment, 0f, 1f) : 1f;
                var (fromPan, fromTilt) = _stops[index - 1];
                var (toPan, toTilt) = _stops[index];
                return (fromPan + (toPan - fromPan) * f, fromTilt + (toTilt - fromTilt) * f);
            }

            distance -= segment;
        }

        return _stops[^1];
    }

    private int StopIndexAt(float distance)
    {
        for (var index = 1; index < _stops.Count; index++)
        {
            if (distance < _segmentLengths[index])
                return index - 1;

            distance -= _segmentLengths[index];
        }

        return Math.Max(_stops.Count - 1, 0);
    }

    private static float AngleDistance(float a, float b)
    {
        return MathF.Abs((a - b + 540f) % 360f - 180f);
    }

    public IStatusProvider.StatusLevel GetStatusLevel() => IStatusProvider.StatusLevel.Success;
    public string GetStatusMessage() => _status;

    /** Runs the scan; off pauses it at the current stop. */
    [Input(Guid = "c306a59f-e21b-478d-94a9-5f6b2d3c7e78")]
    public readonly InputSlot<bool> Run = new(false);

    /** Rising edge returns to the first stop. */
    [Input(Guid = "d417b6a0-f32c-489e-a5ba-607c3e4d8f89")]
    public readonly InputSlot<bool> Restart = new(false);

    [Input(Guid = "e528c7b1-0443-49af-b6cb-718d4f5e909a")]
    public readonly InputSlot<float> PanStart = new(0f);

    [Input(Guid = "f639d8c2-1554-4ab0-87dc-829e5a6fa1ab")]
    public readonly InputSlot<float> PanRange = new(360f);

    /** Degrees between stops along a row at the horizon; about 2/3 of the horizontal field of view keeps 30% overlap. */
    [Input(Guid = "074ae9d3-2665-4bc1-98ed-93af6b70b2bc")]
    public readonly InputSlot<float> PanStep = new(40f);

    [Input(Guid = "185bfae4-3776-4cd2-a9fe-a4b07c81c3cd")]
    public readonly InputSlot<float> TiltMin = new(0f);

    [Input(Guid = "296c0bf5-4887-4de3-8a0f-b5c18d92d4de")]
    public readonly InputSlot<float> TiltMax = new(90f);

    /** Degrees between rows; about 2/3 of the vertical field of view. */
    [Input(Guid = "3a7d1c06-5998-4ef4-9b1a-c6d29ea3e5ef")]
    public readonly InputSlot<float> TiltStep = new(25f);

    /** Tilt (degrees) at which the camera looks along its pan axis; rows thin out towards it. */
    [Input(Guid = "4b8e2d17-6aa9-4f05-8c2b-d7e3afb4f6f0")]
    public readonly InputSlot<float> PoleTilt = new(90f);

    /** Seconds to wait after arrival before capturing: video delay plus depth smoothing. */
    [Input(Guid = "5c9f3e28-7bba-4016-9d3c-e8f4b0c50701")]
    public readonly InputSlot<float> SettleSec = new(0.8f);

    /** Degrees the reported position may differ from the target to count as arrived. */
    [Input(Guid = "6da04f39-8ccb-4127-ae4d-f905c1d61812")]
    public readonly InputSlot<float> ArrivalTolerance = new(0.5f);

    /** Reported pan in degrees, e.g. AxisPtzCamera.Pan. */
    [Input(Guid = "7eb1504a-9ddc-4238-bf5e-0a16d2e72923")]
    public readonly InputSlot<float> CurrentPan = new(0f);

    /** Reported tilt in degrees, e.g. AxisPtzCamera.Tilt. */
    [Input(Guid = "8fc2615b-aeed-4349-806f-1b27e3f83a34")]
    public readonly InputSlot<float> CurrentTilt = new(0f);

    /** ONVIF mapping: degrees of pan per generic unit (180 on AXIS, generic -1..1 = -180..180 degrees). */
    [Input(Guid = "90d3726c-bffe-445a-9170-2c38f4094b45")]
    public readonly InputSlot<float> PanDegreesPerUnit = new(180f);

    /** ONVIF mapping: degrees of tilt per generic unit (90 on AXIS). */
    [Input(Guid = "a1e4837d-c10f-456b-a281-3d4905ba5c56")]
    public readonly InputSlot<float> TiltDegreesPerUnit = new(90f);

    /** ONVIF mapping: generic tilt value at 0 degrees (-1 on AXIS). */
    [Input(Guid = "b2f5948e-d21a-467c-b392-4e5a16cb6d67")]
    public readonly InputSlot<float> TiltZeroAt = new(-1f);

    /** Sweeps the path at SweepSpeed while capturing every frame, instead of stopping at each stop. */
    [Input(Guid = "c417b6a0-f32c-489e-a5ba-607c3e4d8f9a")]
    public readonly InputSlot<bool> Continuous = new(true);

    /** Continuous sweep speed in degrees per second; slower gives sharper frames and denser clouds. */
    [Input(Guid = "c528c7b1-0443-49af-b6cb-718d4f5e90ab")]
    public readonly InputSlot<float> SweepSpeed = new(15f);

    /** Degrees the target may run ahead of the reported position before the sweep waits for the camera. */
    [Input(Guid = "c639d8c2-1554-4ab0-87dc-829e5a6fa1bc")]
    public readonly InputSlot<float> MaxLead = new(6f);

    private enum Phase
    {
        Moving,
        Settling,
        Capturing,
        Captured,
    }

    private const float CaptureWindowSec = 0.6f;

    private readonly List<(float Pan, float Tilt)> _stops = new();
    private readonly List<float> _segmentLengths = new();
    private float _pathLength;
    private float _cursor;
    private double _lastStepTime;
    private (float, float, float, float, float, float, float) _layout = (float.NaN, 0, 0, 0, 0, 0, 0);
    private int _stopIndex;
    private Phase _phase;
    private double _phaseStartTime;
    private bool _wasRestart;
    private int _lastStepFrame = -1;
    private string _status = string.Empty;
}
