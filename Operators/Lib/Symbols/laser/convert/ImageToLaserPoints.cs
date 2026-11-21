#nullable enable
using System;
using System.Runtime.InteropServices;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Graphics.Compat;
using Lib.Utils;

namespace Lib.laser.convert;

/// <summary>The vectorization detector the operator runs.</summary>
public enum ImageToLaserModes
{
    /// <summary>Threshold → skeleton centerlines of thin bright strokes (Thickness 1) or region
    /// outlines of blobs (Thickness &gt; 1).</summary>
    FindPaths = 0,

    /// <summary>Canny edges of the image, traced as thin paths — outlines of everything with
    /// enough contrast, independent of fill.</summary>
    FindContours = 1,
}

/// <summary>Working-resolution cap for the vectorization (larger = finer detail, more CPU).</summary>
public enum ImageToLaserRes
{
    Res256 = 0,
    Res512 = 1,
    Res1024 = 2,
    Res2048 = 3,
}

/// <summary>
/// Vectorizes a texture into ILDA laser points — the graph-side Find Paths / Find Contours
/// stage: bright strokes and edges on a black background become laser shapes. The texture is
/// read back through a staged ring buffer (the vectorization runs one or two frames behind the
/// rendered image, which laser output easily tolerates), downscaled to a square-bounded working
/// resolution with max-pooling (a thin bright line survives the downscale), then traced:
///
/// Find Paths: threshold → Zhang-Suen skeleton → path trace — one lit stroke per line; a blob
/// wider than Thickness switches to region outlines instead (the two-sided ribbon a vectorizer
/// guide's Thickness reduction documents). Find Contours: Gaussian blur → Sobel → non-max
/// suppression → hysteresis → path trace. Shapes are emitted as blanked approach point + evenly
/// spaced lit samples, the encoding the output pipeline splits into shapes, so the output setup's
/// optimizer and safety stage run on the result unchanged.
///
/// Like LaserCamera it is laser content: it registers with the laser content registry, lists as
/// CONTENT in the Output Setup and routes through its OutputRef input; the LaserPoints output
/// feeds LaserPointsToBuffer for in-graph preview. The driver's point budget caps the frame.
/// </summary>
[Guid("7e3c1a52-9b44-4c2e-8f6a-1d2b3c4e5f60")]
internal sealed class ImageToLaserPoints : Instance<ImageToLaserPoints>, ILaserContentSupplier, IStatusProvider
{
    [Output(Guid = "C2D4E6F8-A1B2-4C3D-8E4F-5A6B7C8D9E01")]
    public readonly Slot<StructuredList> LaserPoints = new();

    [Output(Guid = "D5E7F9A1-B3C2-4D4E-9F5A-6B7C8D9E0F12")]
    public readonly Slot<int> PointCount = new();

    public ImageToLaserPoints()
    {
        LaserPoints.UpdateAction += UpdateLaserFrame;
        PointCount.UpdateAction += UpdateLaserFrame;
        LaserContentRegistry.Register(this);
    }

    StructuredList ILaserContentSupplier.GetLaserPoints(EvaluationContext context) => LaserPoints.GetValue(context);

    bool ILaserContentSupplier.GetUpdateEnabled(EvaluationContext context) => Update.GetValue(context);

    IInputSlot ILaserContentSupplier.UpdateInput => Update;

    bool ILaserContentSupplier.TryGetRoutingInput(out Symbol.Child.Input input)
    {
        return TryGetSavedInput(RoutingInputId, out input);
    }

    /// <summary>Looks this op's symbol-child input up without evaluating it — the setup reads and
    /// writes routing against the saved graph.</summary>
    private bool TryGetSavedInput(Guid inputId, out Symbol.Child.Input input)
    {
        input = null!;
        var parent = Parent;
        if (parent == null || !parent.Symbol.Children.TryGetValue(SymbolChildId, out var child))
            return false;

        return child.Inputs.TryGetValue(inputId, out input);
    }

    /// <summary>The OutputRef input's id as declared in this symbol's definition.</summary>
    private static readonly Guid RoutingInputId = new("A7C1E382-5B94-4D26-8F30-9D1A2B3C4E65");

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
    {
        if (_lastError != null)
            return IStatusProvider.StatusLevel.Error;

        if (!Texture.HasInputConnections)
            return IStatusProvider.StatusLevel.Warning;

        if (_unsupportedFormat != null || _overBudget)
            return IStatusProvider.StatusLevel.Warning;

        return OutputRef.Value == Guid.Empty
                   ? IStatusProvider.StatusLevel.Notice
                   : IStatusProvider.StatusLevel.Success;
    }

    string? IStatusProvider.GetStatusMessage()
    {
        if (_lastError != null)
            return $"Update failed, streaming an empty frame: {_lastError}";

        if (!Texture.HasInputConnections)
            return "No Texture connected, nothing to vectorize.";

        if (_unsupportedFormat != null)
            return $"Unsupported texture format {_unsupportedFormat} - use B8G8R8A8, R8G8B8A8, R16G16B16A16, R8 or R32_Float.";

        if (_overBudget)
            return $"Over budget: the frame is capped at {_lastCap:N0} points - raise PointSpacing, Denoise or MaxRes.";

        if (OutputRef.Value == Guid.Empty)
            return "Not routed to a laser output. Drop it on one in the Output Setup.";

        return Update.Value
                   ? $"{_lastPointCount:N0} points, {Session.Stats.Shapes} shapes ({Session.Stats.WorkWidth}x{Session.Stats.WorkHeight})"
                   : $"Frozen at {_lastPointCount:N0} points";
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        LaserContentRegistry.Unregister(this);
        _readAccess.Dispose();
    }

    // --------------------------------------------------------------------------------------------
    // Update
    // --------------------------------------------------------------------------------------------

    private void UpdateLaserFrame(EvaluationContext context)
    {
        // TiXL terminates the process on an unhandled operator exception. Readback and
        // vectorization must degrade to an empty frame, never crash the editor.
        try
        {
            UpdateInternal(context);
        }
        catch (Exception e)
        {
            ReportFailure(e);
            SetEmptyOutput();
        }
    }

    private void UpdateInternal(EvaluationContext context)
    {
        var texture = Texture.GetValue(context);
        if (texture == null || texture.IsDisposed)
        {
            _readPending = false;
            SetEmptyOutput();
            return;
        }

        // Frozen: keep the last frame; pending readbacks stall with it and the next
        // unfrozen Update resumes the stream.
        if (_hasFrame && !Update.GetValue(context))
            return;

        // Deliver completed staged copies, then keep one readback in flight: each pull
        // re-requests the current texture, so animated content streams at pump rate with
        // roughly one to two frames of latency.
        _readAccess.Update();
        if (!_readPending && _readAccess.InitiateReadAndConvert(texture, OnReadComplete))
            _readPending = true;

        var paramsChanged = ReadParams(context);
        if (!_hasData || (!paramsChanged && !_newData))
            return;

        _newData = false;
        var cap = LaserContentRegistry.PointBudget;
        if (cap <= 0 || cap > MaxOutputPoints)
            cap = MaxOutputPoints;
        _lastCap = cap;

        Session.Run(new LaserVectorizeSession.Params
                        {
                            Mode = (LaserVectorizeSession.DetectMode)_mode,
                            Threshold = _threshold,
                            Thickness = _thickness,
                            BlurSize = _blurSize,
                            Denoise = _denoise,
                            MaxPaths = _maxPaths,
                            PointSpacing = _pointSpacing,
                            UseColor = _useColor,
                            Brightness = _brightness,
                            PointCap = cap,
                            EndRepeat = _endRepeat,
                            InFade = _inFade,
                            OutFade = _outFade,
                            MinPathLengthPct = _pathMinLengthPct,
                            MaxPathLengthPct = _pathMaxLengthPct,
                        });

        LaserPoints.Value = Finish(Session.Count, Session.Buffer);
        PointCount.Value = Session.Count;
        _overBudget = Session.Stats.OverBudget;
        _hasFrame = true;
        _lastPointCount = Session.Count;
        LaserPoints.DirtyFlag.Trigger = DirtyFlagTrigger.Animated;
        PointCount.DirtyFlag.Trigger = DirtyFlagTrigger.Animated;
    }

    /// <summary>Snapshots the op inputs into the run parameters; returns whether any of them
    /// changed since the last vectorize (a changed parameter re-runs on the cached CPU copy).</summary>
    private bool ReadParams(EvaluationContext context)
    {
        _mode = Math.Clamp((int)Mode.GetValue(context), 0, 1);
        _threshold = Math.Clamp(Threshold.GetValue(context), 0f, 1f);
        _thickness = Math.Clamp((int)Thickness.GetValue(context), 1, 64);
        _blurSize = Math.Clamp((int)BlurSize.GetValue(context), 0, 4);
        _denoise = Math.Max(0, (int)Denoise.GetValue(context));
        _maxPaths = Math.Clamp((int)MaxPaths.GetValue(context), 1, 4096);
        _pointSpacing = Math.Clamp(PointSpacing.GetValue(context), 0.5f, 64f);
        _useColor = UseColor.GetValue(context);
        _brightness = Math.Clamp(Brightness.GetValue(context), 0f, 1f);
        _endRepeat = Math.Clamp((int)EndRepeat.GetValue(context), 0, 16);
        _inFade = Math.Clamp(InFade.GetValue(context), 0f, 1f);
        _outFade = Math.Clamp(OutFade.GetValue(context), 0f, 1f);
        _pathMinLengthPct = Math.Clamp(PathMinLength.GetValue(context), 0f, 100f);
        _pathMaxLengthPct = Math.Clamp(PathMaxLength.GetValue(context), 0f, 2000f);
        var maxRes = Math.Clamp((int)MaxRes.GetValue(context), 0, 3);

        if (_lastMaxRes != maxRes)
        {
            _lastMaxRes = maxRes;
            _resChanged = true;
        }

        if (_resChanged)
        {
            _resChanged = false;
            return true;
        }

        var changed = _mode != _lastMode
                      || !_threshold.Equals(_lastThreshold)
                      || _thickness != _lastThickness
                      || _blurSize != _lastBlurSize
                      || _denoise != _lastDenoise
                      || _maxPaths != _lastMaxPaths
                      || !_pointSpacing.Equals(_lastPointSpacing)
                      || _useColor != _lastUseColor
                      || !_brightness.Equals(_lastBrightness)
                      || _endRepeat != _lastEndRepeat
                      || !_inFade.Equals(_lastInFade)
                      || !_outFade.Equals(_lastOutFade)
                      || !_pathMinLengthPct.Equals(_lastPathMinLengthPct)
                      || !_pathMaxLengthPct.Equals(_lastPathMaxLengthPct);

        _lastMode = _mode;
        _lastThreshold = _threshold;
        _lastThickness = _thickness;
        _lastBlurSize = _blurSize;
        _lastDenoise = _denoise;
        _lastMaxPaths = _maxPaths;
        _lastPointSpacing = _pointSpacing;
        _lastUseColor = _useColor;
        _lastBrightness = _brightness;
        _lastEndRepeat = _endRepeat;
        _lastInFade = _inFade;
        _lastOutFade = _outFade;
        _lastPathMinLengthPct = _pathMinLengthPct;
        _lastPathMaxLengthPct = _pathMaxLengthPct;
        return changed;
    }

    private int _lastMaxRes = -1;
    private bool _resChanged;

    // --------------------------------------------------------------------------------------------
    // Readback: staged ring buffer → full CPU copy → working buffers
    // --------------------------------------------------------------------------------------------

    private void OnReadComplete(TextureReadAccess.ReadRequestItem request)
    {
        var texture = request.CpuAccessTexture;
        if (texture.IsDisposed)
        {
            _readPending = false;
            return;
        }

        // This callback runs inside TextureReadAccess.Update: an exception here would propagate
        // out with the request already consumed while _readPending is still true - the op would
        // never initiate another readback (silent permanent freeze). Everything degrades to the
        // last frame instead, per the op's own failure convention.
        try
        {
            // Nested map inside TextureReadAccess.Update's bracket - the pattern the other
            // TextureReadAccess consumer in Lib uses; the outer bracket unmaps after us.
            ResourceManager.Device.ImmediateContext.MapSubresource(texture, 0, 0, MapMode.Read, MapFlags.None, out var sourceStream);
            try
            {
                var desc = texture.Description;
                var formatId = ToFormatId(desc.Format);
                if (formatId < 0)
                {
                    _unsupportedFormat = desc.Format.ToString();
                    return;
                }

                _unsupportedFormat = null;
                var bytesPerPixel = BytesPerPixelOf(formatId);
                var rowPitch = desc.Width * bytesPerPixel;
                var totalBytes = rowPitch * desc.Height;
                if (_stagingBytes.Length < totalBytes)
                    _stagingBytes = new byte[Math.Max(totalBytes, _stagingBytes.Length * 2)];

                sourceStream.Position = 0;
                sourceStream.Read(_stagingBytes, 0, totalBytes);

                if (!Session.BeginCopy(desc.Width, desc.Height, _maxResValues[_lastMaxRes]))
                {
                    _lastError = "Texture size out of range";
                    return;
                }

                Session.CopyFrame(_stagingBytes, rowPitch, desc.Width, desc.Height, formatId);
                _hasData = true;
                _newData = true;
            }
            finally
            {
                sourceStream.Dispose();
            }
        }
        catch (Exception e)
        {
            _lastError = $"Readback failed: {e.Message}";
            Log.Debug($"ImageToLaserPoints: readback failed, keeping the last frame: {e}");
        }
        finally
        {
            _readPending = false;
        }
    }

    private static int ToFormatId(Format format)
    {
        return format switch
               {
                   Format.B8G8R8A8_UNorm => LaserVectorizeSession.FmtBgra8,
                   Format.R8G8B8A8_UNorm => LaserVectorizeSession.FmtRgba8,
                   Format.R16G16B16A16_Float => LaserVectorizeSession.FmtRgba16F,
                   Format.R8_UNorm => LaserVectorizeSession.FmtR8,
                   Format.R32_Float => LaserVectorizeSession.FmtR32F,
                   _ => -1,
               };
    }

    private static int BytesPerPixelOf(int formatId)
    {
        return formatId switch
               {
                   LaserVectorizeSession.FmtRgba16F => 8,
                   LaserVectorizeSession.FmtR8 => 1,
                   LaserVectorizeSession.FmtR32F => 4,
                   _ => 4,
               };
    }

    // --------------------------------------------------------------------------------------------
    // Publish
    // --------------------------------------------------------------------------------------------

    /// <summary>Publishes the emitted frame: republished as the same list when the frame did not
    /// change, exact-size copy of the session buffer otherwise (scratch reused).</summary>
    private StructuredList Finish(int count, LaserPoint[] source)
    {
        count = Math.Min(count, source.Length);

        if (_published != null && _published.TypedElements.Length == count
                                && _lastPointCount == count
                                && FrameUnchanged(source, count))
        {
            return _published;
        }

        var resultArray = new LaserPoint[count];
        Array.Copy(source, resultArray, count);
        _published = new StructuredList<LaserPoint>(resultArray);
        return _published;
    }

    private bool FrameUnchanged(LaserPoint[] source, int count)
    {
        var published = _published!.TypedElements;
        if (published.Length != count)
            return false;

        for (var i = 0; i < count; i++)
        {
            var a = source[i];
            var b = published[i];
            if (a.X != b.X || a.Y != b.Y || a.R != b.R || a.G != b.G || a.B != b.B || a.I != b.I)
                return false;
        }

        return true;
    }

    private void SetEmptyOutput()
    {
        LaserPoints.Value = s_emptyList;
        PointCount.Value = 0;
        LaserPoints.DirtyFlag.Trigger = DirtyFlagTrigger.Animated;
        PointCount.DirtyFlag.Trigger = DirtyFlagTrigger.Animated;
        _hasFrame = false;
        _lastPointCount = 0;
        _overBudget = false;
    }

    private void ReportFailure(Exception e)
    {
        var now = Environment.TickCount64;
        if (e.Message == _lastError && now - _lastErrorLogMs < 5000)
            return;

        _lastError = e.Message;
        _lastErrorLogMs = now;
        Log.Error($"ImageToLaserPoints: update failed, outputting empty frame - {e.Message}", this);
    }

    private static readonly StructuredList s_emptyList = new StructuredList<LaserPoint>(Array.Empty<LaserPoint>());

    // --------------------------------------------------------------------------------------------
    // Fields
    // --------------------------------------------------------------------------------------------

    public LaserVectorizeSession Session { get; } = new();

    private byte[] _stagingBytes = [];
    private StructuredList<LaserPoint>? _published;

    private bool _hasData;
    private bool _hasFrame;
    private bool _newData;
    private bool _readPending;
    private bool _overBudget;
    private string? _unsupportedFormat;
    private string? _lastError;
    private long _lastErrorLogMs;
    private int _lastPointCount;
    private int _lastCap;

    private int _mode;
    private float _threshold;
    private int _thickness;
    private int _blurSize;
    private int _denoise;
    private int _maxPaths;
    private float _pointSpacing;
    private bool _useColor;
    private float _brightness;

    private bool _lastUseColor;
    private int _lastMode = -1;
    private float _lastThreshold = -1;
    private int _lastThickness = -1;
    private int _lastBlurSize = -1;
    private int _lastDenoise = -1;
    private int _lastMaxPaths = -1;
    private float _lastPointSpacing = -1;
    private int _lastEndRepeat = -1;
    private float _lastInFade = -1f;
    private float _lastOutFade = -1f;
    private int _endRepeat;
    private float _inFade;
    private float _outFade;
    private float _lastPathMinLengthPct = -1f;
    private float _lastPathMaxLengthPct = -1f;
    private float _pathMinLengthPct;
    private float _pathMaxLengthPct;
    private float _lastBrightness = -1;

    private static readonly int[] _maxResValues = [256, 512, 1024, 2048];

    private const int MaxOutputPoints = 1 << 18;

    private readonly TextureReadAccess _readAccess = new();

    [Input(Guid = "3F6E1A90-7C2B-4E3D-9A41-5B8C2D7E1F60")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "9D2F4B18-3E57-4C86-B2A9-1F60D3C47E25", MappedType = typeof(ImageToLaserModes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "C5E8A273-1B94-4F02-8D36-7A91B4C2E5F8")]
    public readonly InputSlot<float> Threshold = new(0.2f);

    [Input(Guid = "2B7D9C14-6A53-4E91-BC08-3F42D8A95E17")]
    public readonly InputSlot<bool> UseColor = new();

    [Input(Guid = "E8F4A261-9C3D-4E57-8B26-4F1A5D3C7E08")]
    public readonly InputSlot<int> BlurSize = new(1);

    [Input(Guid = "E4A1B385-2C76-4D02-9F53-8B1C6D2E4A79")]
    public readonly InputSlot<int> Thickness = new(1);

    [Input(Guid = "71C3E9A2-8D54-4B16-AE27-9C4F1B2D3E58")]
    public readonly InputSlot<int> Denoise = new(2);

    [Input(Guid = "5A8F2D61-9C3B-4E74-BD15-2E7C9A4B1D36")]
    public readonly InputSlot<int> MaxPaths = new(256);

    [Input(Guid = "8C1E5B39-4A72-4D86-9E20-6B3F7C1A2D45")]
    public readonly InputSlot<float> PointSpacing = new(4f);

    [Input(Guid = "D3B7A164-5E28-4C93-AF06-1B9C2D3E4F57", MappedType = typeof(ImageToLaserRes))]
    public readonly InputSlot<int> MaxRes = new();

    [Input(Guid = "B6E2C948-7D13-4A65-8C04-2F1A5B6C7D84")]
    public readonly InputSlot<float> Brightness = new(1f);

    /// <summary>Extra lit samples held at each shape's end position, so the scanner settles on the
    /// real endpoint instead of rounding it off. A dot's dwell time grows with it too - its point intensity.</summary>
    [Input(Guid = "fd3cdd65-3155-4082-880a-ea24bfe5eab9")]
    public readonly InputSlot<int> EndRepeat = new(2);

    /// <summary>Ramps each shape's first samples up from this dim level to full: the scanner is
    /// still accelerating when a path starts, which otherwise blooms the start point. 0 = off.</summary>
    [Input(Guid = "70ab0278-c054-4175-8025-d73771ef2fd8")]
    public readonly InputSlot<float> InFade = new(0f);

    /// <summary>Ramps the end-repeat samples down, so finishing a shape doesn't leave a bright
    /// pause dot of its own. 0 = the repeats hold at full brightness.</summary>
    [Input(Guid = "71b043c3-ae4f-4b33-a48c-93a33ddca3ca")]
    public readonly InputSlot<float> OutFade = new(0f);

    /// <summary>Drops shapes shorter than this percentage of the working image's larger dimension -
    /// noise specks that would cost a blank jump each. 0 keeps everything.</summary>
    [Input(Guid = "ff82f232-0a87-4eba-ae83-e69a803cb2a8")]
    public readonly InputSlot<float> PathMinLength = new(0f);

    /// <summary>Drops shapes longer than this percentage of the working image's larger dimension -
    /// runaway background strokes. 0 keeps everything.</summary>
    [Input(Guid = "80a65286-1404-437a-b421-baf62a8428c9")]
    public readonly InputSlot<float> PathMaxLength = new(0f);

    [Input(Guid = "F9A4D175-6E32-4B58-9D16-3C8E1B2A4C75")]
    public readonly InputSlot<bool> Update = new(true);

    [Input(Guid = "A7C1E382-5B94-4D26-8F30-9D1A2B3C4E65")]
    public readonly InputSlot<Guid> OutputRef = new();
}
