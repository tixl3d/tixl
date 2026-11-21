using System;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using T3.Core.Logging;
using T3.Core.Operator.Interfaces;
using T3.Core.Utils.Geometry;

namespace Lib.io.video.depthanything;

/// <summary>
/// Camera pose from an AXIS PTZ camera's own pan/tilt readout (VAPIX), for scanning with a pan/tilt camera.
/// Unlike a pose estimated from depth it cannot drift, so a full sweep closes into one consistent half-sphere.
/// </summary>
[Guid("7c3e9a51-2f84-4d6b-b1e0-5a9d8c2f4e17")]
public sealed class AxisPtzCamera : Instance<AxisPtzCamera>, ICamera
{
    [Output(Guid = "8d4fa062-3095-4e7c-82f1-6bae9d305f28", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<object> Camera = new();

    [Output(Guid = "9e50b173-41a6-4f8d-93a2-7cbf0e416a39", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Pan = new();

    [Output(Guid = "af61c284-52b7-409e-a4b3-8dc01f527b4a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Tilt = new();

    [Output(Guid = "b072d395-63c8-41af-b5c4-9ed12a638c5b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Zoom = new();

    [Output(Guid = "c183e4a6-74d9-42b0-86d5-afe23b749d6c", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> Status = new();

    /** Vertical field of view in degrees at the current zoom, e.g. for DepthAnything's FieldOfView. */
    [Output(Guid = "d294f5b7-85ea-43c1-97e6-b0f34c85ae80", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> VerticalFov = new();

    public AxisPtzCamera()
    {
        Camera.UpdateAction = Update;
        Pan.UpdateAction = Update;
        Tilt.UpdateAction = Update;
        Zoom.UpdateAction = Update;
        Status.UpdateAction = Update;
        VerticalFov.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var active = Active.GetValue(context);
        var address = (Address.GetValue(context) ?? string.Empty).Trim();
        var user = User.GetValue(context) ?? string.Empty;
        var password = Password.GetValue(context) ?? string.Empty;
        var pollRate = Math.Clamp(PollRate.GetValue(context), 1f, 60f);
        var delayMs = Math.Clamp(DelayMs.GetValue(context), 0f, 2000f);
        var wideFieldOfView = Math.Clamp(FieldOfView.GetValue(context), 1f, 179f);
        var aspect = Math.Max(AspectRatio.GetValue(context), 0.01f);
        var panSign = InvertPan.GetValue(context) ? -1f : 1f;
        var tiltSign = InvertTilt.GetValue(context) ? -1f : 1f;
        var mountDegrees = MountRotation.GetValue(context);
        var referencePan = ReferencePan.GetValue(context);
        var referenceTilt = ReferenceTilt.GetValue(context);

        _pollIntervalMs = (int)(1000f / pollRate);
        var connection = active && address.Length > 0 ? $"{address}|{user}|{password}" : null;
        if (!string.Equals(connection, _connection, StringComparison.Ordinal))
        {
            StopPolling();
            _connection = connection;
            if (connection != null)
                StartPolling(address, user, password);
        }

        float pan, tilt, zoom;
        var hasPose = TryGetPose(Stopwatch.GetTimestamp() - (long)(delayMs / 1000.0 * Stopwatch.Frequency), out pan, out tilt, out zoom);
        if (!hasPose)
        {
            pan = tilt = zoom = 0;
        }

        // Orientation in the depth's OpenCV frame (x right, y down, +Z forward). The axis signs were fitted against the
        // measured image motion of an AXIS P5635-E: pan inverted, tilt as reported.
        // The reference view becomes straight ahead and upright, so a sideways or flipped mount still builds an upright scan
        var reference = Quaternion.Inverse(PtzOrientation(referencePan, referenceTilt, panSign, tiltSign));
        var mount = Quaternion.CreateFromYawPitchRoll(mountDegrees.Y * MathF.PI / 180f, mountDegrees.X * MathF.PI / 180f,
                                                      mountDegrees.Z * MathF.PI / 180f);
        var orientation = Quaternion.Normalize(Quaternion.Multiply(Quaternion.Multiply(mount, reference),
                                                                   PtzOrientation(pan, tilt, panSign, tiltSign)));

        // The camera mechanics live in the depth's OpenCV frame (y down, +Z forward); the scan is built in TiXL's
        // world (y up, looking along -Z). A half turn about X converts one into the other without mirroring.
        orientation = Quaternion.Normalize(Quaternion.Multiply(OpenCvToWorld, orientation));

        // Zooming narrows the view by the lens magnification: tan(fov/2) scales with 1/magnification
        var magnification = ZoomMagnification(hasPose ? zoom : 1f);
        var fieldOfView = 2f * MathF.Atan(MathF.Tan(wideFieldOfView * MathF.PI / 360f) / magnification) * 180f / MathF.PI;
        VerticalFov.Value = fieldOfView;

        SetCameraPose(Vector3.Transform(Vector3.UnitZ, orientation), Vector3.Transform(-Vector3.UnitY, orientation), fieldOfView, aspect);

        Pan.Value = pan;
        Tilt.Value = tilt;
        Zoom.Value = zoom;
        Status.Value = connection == null
                           ? "Inactive"
                           : _lastError != null
                               ? "Error: " + _lastError
                               : hasPose
                                   ? $"pan {pan:F1} tilt {tilt:F1} zoom {zoom:F0} ({_samplesPerSecond:F0} Hz)"
                                   : "Connecting...";
        Camera.Value = this;
    }

    /**
     * Lens magnification for a VAPIX zoom reading (1..9999). Measured on an AXIS P5635-E by chaining the image scale between
     * neighbouring zoom steps up to 2500; beyond that the readings are close to linear in focal length, so the table ends
     * at the 30x optical zoom the data sheet gives for 9999.
     */
    private static float ZoomMagnification(float zoom)
    {
        if (zoom <= ZoomTable[0].Zoom)
            return ZoomTable[0].Magnification;

        for (var index = 1; index < ZoomTable.Length; index++)
        {
            var (z1, m1) = ZoomTable[index];
            if (zoom > z1)
                continue;

            var (z0, m0) = ZoomTable[index - 1];
            return m0 + (m1 - m0) * (zoom - z0) / (z1 - z0);
        }

        return ZoomTable[^1].Magnification;
    }

    private static readonly (float Zoom, float Magnification)[] ZoomTable =
        [(1, 1f), (500, 2.44f), (1000, 3.92f), (1500, 5.47f), (2000, 7.10f), (2500, 8.78f), (9999, 30f)];

    /** Camera orientation for a pan/tilt reading: pan about the base's up axis (-Y), then tilt about the camera's right axis (+X). */
    private static Quaternion PtzOrientation(float pan, float tilt, float panSign, float tiltSign)
    {
        var panRotation = Quaternion.CreateFromAxisAngle(-Vector3.UnitY, panSign * pan * MathF.PI / 180f);
        var tiltRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, tiltSign * tilt * MathF.PI / 180f);
        return Quaternion.Multiply(panRotation, tiltRotation);
    }

    /** Builds the camera the same way DepthAnything's CamReference does, so scan operators unproject through either identically. */
    private void SetCameraPose(Vector3 forward, Vector3 up, float fovDegrees, float aspect)
    {
        _cameraDefinition = new CameraDefinition
                                {
                                    NearFarClip = new Vector2(0.01f, 1000f),
                                    LensShift = Vector2.Zero,
                                    PositionOffset = Vector3.Zero,
                                    Position = Vector3.Zero,
                                    Target = forward,
                                    Up = up,
                                    AspectRatio = aspect,
                                    FieldOfView = fovDegrees * MathF.PI / 180f,
                                    Roll = 0,
                                    RotationOffset = Vector3.Zero,
                                    OffsetAffectsTarget = false
                                };
        _cameraDefinition.BuildProjectionMatrices(out var camToClipSpace, out var worldToCamera);
        CameraPosition = Vector3.Zero;
        CameraTarget = forward;
        CameraRoll = 0;
        CameraToClipSpace = camToClipSpace;
        WorldToCamera = worldToCamera;
    }

    #region Polling
    private void StartPolling(string address, string user, string password)
    {
        _lastError = null;
        _sampleCount = 0;
        var baseAddress = address.Contains("://", StringComparison.Ordinal) ? address.TrimEnd('/') : "http://" + address.TrimEnd('/');
        var cancellation = new CancellationTokenSource();
        _pollCancellation = cancellation;
        _pollTask = Task.Run(() => PollLoop(baseAddress, user, password, cancellation.Token));
    }

    private void StopPolling()
    {
        _pollCancellation?.Cancel();
        _pollCancellation = null;
        _pollTask = null;
    }

    private async Task PollLoop(string baseAddress, string user, string password, CancellationToken token)
    {
        // Digest auth is negotiated by the handler; the camera answers a position query in a few milliseconds.
        // A LAN camera must not go through the system proxy, which made every query time out.
        using var handler = new HttpClientHandler
                                {
                                    Credentials = new NetworkCredential(user, password),
                                    PreAuthenticate = true,
                                    UseProxy = false,
                                };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var url = baseAddress + "/axis-cgi/com/ptz.cgi?query=position";
        var rateWindowStart = Stopwatch.GetTimestamp();
        var rateWindowCount = 0;
        string? loggedError = null;

        while (!token.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var body = await client.GetStringAsync(url, token).ConfigureAwait(false);
                var finished = Stopwatch.GetTimestamp();
                if (TryParsePosition(body, out var pan, out var tilt, out var zoom))
                {
                    // The reading is taken somewhere inside the request; its midpoint is the best estimate
                    AddSample(started + (finished - started) / 2, pan, tilt, zoom);
                    _lastError = null;
                    loggedError = null;
                    rateWindowCount++;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                _lastError = e.Message;
                if (!string.Equals(loggedError, e.Message, StringComparison.Ordinal))
                {
                    loggedError = e.Message;
                    Log.Warning($"[AxisPtzCamera] Position query failed: {e.Message}", this);
                }
            }

            var now = Stopwatch.GetTimestamp();
            if (now - rateWindowStart > Stopwatch.Frequency)
            {
                _samplesPerSecond = rateWindowCount * (float)Stopwatch.Frequency / (now - rateWindowStart);
                rateWindowStart = now;
                rateWindowCount = 0;
            }

            var elapsedMs = (int)((now - started) * 1000 / Stopwatch.Frequency);
            try
            {
                await Task.Delay(Math.Max(0, _pollIntervalMs - elapsedMs), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static bool TryParsePosition(string body, out float pan, out float tilt, out float zoom)
    {
        pan = tilt = zoom = 0;
        var found = 0;
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOf('=');
            if (separator <= 0
                || !float.TryParse(line.AsSpan(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                continue;

            switch (line[..separator])
            {
                case "pan":
                    pan = value;
                    found++;
                    break;
                case "tilt":
                    tilt = value;
                    found++;
                    break;
                case "zoom":
                    zoom = value;
                    break;
            }
        }

        return found == 2;
    }

    private void AddSample(long ticks, float pan, float tilt, float zoom)
    {
        lock (_sampleLock)
        {
            var index = _sampleCount % SampleCapacity;
            _sampleTicks[index] = ticks;
            _samplePan[index] = pan;
            _sampleTilt[index] = tilt;
            _sampleZoom[index] = zoom;
            _sampleCount++;
        }
    }

    /** Pose at <paramref name="ticks"/>, interpolated between the bracketing readings; clamped to the oldest/newest one. */
    private bool TryGetPose(long ticks, out float pan, out float tilt, out float zoom)
    {
        lock (_sampleLock)
        {
            pan = tilt = zoom = 0;
            if (_sampleCount == 0)
                return false;

            var available = Math.Min(_sampleCount, SampleCapacity);
            var newest = (_sampleCount - 1) % SampleCapacity;
            var later = newest;
            for (var age = 0; age < available; age++)
            {
                var index = (_sampleCount - 1 - age) % SampleCapacity;
                if (_sampleTicks[index] <= ticks)
                {
                    if (index == later)
                    {
                        pan = _samplePan[index];
                        tilt = _sampleTilt[index];
                        zoom = _sampleZoom[index];
                        return true;
                    }

                    var span = _sampleTicks[later] - _sampleTicks[index];
                    var f = span > 0 ? (float)(ticks - _sampleTicks[index]) / span : 0f;
                    pan = LerpAngle(_samplePan[index], _samplePan[later], f);
                    tilt = _sampleTilt[index] + (_sampleTilt[later] - _sampleTilt[index]) * f;
                    zoom = _sampleZoom[index] + (_sampleZoom[later] - _sampleZoom[index]) * f;
                    return true;
                }

                later = index;
            }

            // Older than every reading kept: the oldest one is the closest
            pan = _samplePan[later];
            tilt = _sampleTilt[later];
            zoom = _sampleZoom[later];
            return true;
        }
    }

    /** Pan wraps at +-180 degrees. */
    private static float LerpAngle(float a, float b, float f)
    {
        var delta = (b - a + 540f) % 360f - 180f;
        return a + delta * f;
    }
    #endregion

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            StopPolling();

        base.Dispose(isDisposing);
    }

    #region ICamera
    public Vector3 CameraPosition { get; set; }
    public Vector3 CameraTarget { get; set; }
    public float CameraRoll { get; set; }
    public CameraDefinition CameraDefinition => _cameraDefinition;
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 CameraToClipSpace { get; set; }
    #endregion

    [Input(Guid = "d294f5b7-85ea-43c1-97e6-b0f34c85ae7d")]
    public readonly InputSlot<bool> Active = new(true);

    /// <summary>Camera host or base URL, e.g. 10.0.0.111.</summary>
    [Input(Guid = "e3a506c8-96fb-44d2-a8f7-c1045d96bf8e")]
    public readonly InputSlot<string> Address = new("10.0.0.111");

    [Input(Guid = "f4b617d9-a70c-45e3-b908-d2156ea7c09f")]
    public readonly InputSlot<string> User = new(string.Empty);

    [Input(Guid = "05c728ea-b81d-46f4-8a19-e3267fb8d1a0")]
    public readonly InputSlot<string> Password = new(string.Empty);

    /// <summary>Position queries per second.</summary>
    [Input(Guid = "16d839fb-c92e-4705-9b2a-f4378ac9e2b1")]
    public readonly InputSlot<float> PollRate = new(30f);

    /// <summary>
    /// How far the pose lags behind the live PTZ position, so it matches the video frame the depth was computed from:
    /// the stream's latency plus the depth model's. The P5635-E stream measured 80 ms on a LAN.
    /// </summary>
    [Input(Guid = "27e94a0c-da3f-4816-ac3b-05489bdaf3c2")]
    public readonly InputSlot<float> DelayMs = new(120f);

    /// <summary>
    /// Vertical field of view in degrees at the widest zoom (38.4 for the P5635-E per its data sheet, 37.2 measured).
    /// The camera narrows it by the lens magnification of the current zoom reading.
    /// </summary>
    [Input(Guid = "38fa5b1d-eb4c-4927-bd4c-1659ac04e4d3")]
    public readonly InputSlot<float> FieldOfView = new(38.4f);

    [Input(Guid = "490b6c2e-fc5d-4a38-8e5d-276abd15f5e4")]
    public readonly InputSlot<float> AspectRatio = new(16f / 9f);

    [Input(Guid = "5a1c7d3f-0d6e-4b49-9f6e-387bce2606f5")]
    public readonly InputSlot<bool> InvertPan = new(true);

    [Input(Guid = "6b2d8e40-1e7f-4c5a-a07f-498cdf371706")]
    public readonly InputSlot<bool> InvertTilt = new(false);

    /// <summary>Rotates the whole scan (pitch, yaw, roll in degrees), e.g. to stand the half-sphere of a sideways-mounted camera upright.</summary>
    [Input(Guid = "7c3e9f51-2f80-4d6b-b18f-5a9de0482817")]
    public readonly InputSlot<Vector3> MountRotation = new(Vector3.Zero);

    /// <summary>Pan reading (degrees) of the view that becomes the scan's straight-ahead, upright direction, e.g. the middle of a tour.</summary>
    [Input(Guid = "8d4fa062-3091-4e7c-82f1-6bae9d305f29")]
    public readonly InputSlot<float> ReferencePan = new(0f);

    /// <summary>Tilt reading (degrees) of that reference view.</summary>
    [Input(Guid = "9e50b173-41a2-4f8d-93a2-7cbf0e416a3a")]
    public readonly InputSlot<float> ReferenceTilt = new(0f);

    private static readonly Quaternion OpenCvToWorld = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);

    private const int SampleCapacity = 256;
    private readonly object _sampleLock = new();
    private readonly long[] _sampleTicks = new long[SampleCapacity];
    private readonly float[] _samplePan = new float[SampleCapacity];
    private readonly float[] _sampleTilt = new float[SampleCapacity];
    private readonly float[] _sampleZoom = new float[SampleCapacity];
    private int _sampleCount;
    private volatile int _pollIntervalMs = 33;
    private volatile float _samplesPerSecond;
    private volatile string? _lastError;
    private string? _connection;
    private CancellationTokenSource? _pollCancellation;
    private Task? _pollTask;
    private CameraDefinition _cameraDefinition;
}
