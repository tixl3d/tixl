#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;
using SharpDX.Direct3D11;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Editor.Gui.Styling;
using Buffer = SharpDX.Direct3D11.Buffer;
using Color = T3.Core.DataTypes.Vector.Color;
using Int2 = T3.Core.DataTypes.Vector.Int2;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Surface = T3.Core.Output.Surface;
using PixelShader = T3.Core.DataTypes.PixelShader;
using VertexShader = T3.Core.DataTypes.VertexShader;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// What a calibration session projects onto the wall on top of the composite: the aim crosshair, the discs
/// of the straightened photo around reference points, and the annotation lines and markers. The views
/// re-state the session state every frame it is active; it expires on its own one frame after they stop.
/// <see cref="OutputCompositor"/> calls the collect and draw entry points in frame order.
/// </summary>
internal static class CalibrationOverlay
{
    /// <summary>
    /// Where a tool is currently being aimed on <paramref name="surfaceId"/>, in surface meters. Projected as
    /// a crosshair so the point can be placed against a physical feature *before* the drag starts — until the
    /// first press there is nothing else on the wall to aim with.
    /// </summary>
    public static void SetAimPoint(Guid surfaceId, Vector2 inSurface)
    {
        _aim.Set(new AimPoint(surfaceId, inSurface), ImGui.GetFrameCount());
    }

    /// <summary>
    /// Projects discs of a surface's straightened photo around its reference points, for calibrating the pin
    /// against the real wall.
    /// </summary>
    public static void SetCalibrationPhoto(Guid surfaceId, ShaderResourceView srv, Vector2 uvMin, Vector2 uvMax, float radiusOfHeight)
    {
        var uv = new Vector4(uvMin.X, uvMin.Y, uvMax.X, uvMax.Y);
        _photo.Set(new CalibrationPhoto(surfaceId, srv, uv, radiusOfHeight), ImGui.GetFrameCount());
    }

    /// <summary>
    /// Marks one of a surface's annotation lines as the one being edited, so the projected composite draws it
    /// thick and pulsing on the wall (the whole point of calibrating is watching that projection).
    /// </summary>
    public static void EmphasizeAnnotation(Guid surfaceId, int index)
    {
        _emphasized.Set(new EmphasizedAnnotation(surfaceId, index), ImGui.GetFrameCount());
    }

    /// <summary>Aligned to within a fraction of a degree reads as good; off by a few, as work left to do.</summary>
    public static Color AlignmentColor(float deviationInDegrees)
    {
        var t = Math.Clamp((deviationInDegrees - LineRectifier.AlignedDegrees)
                           / (LineRectifier.MisalignedDegrees - LineRectifier.AlignedDegrees), 0f, 1f);
        return Color.MixOkLab(UiColors.StatusOkay, UiColors.StatusWarning, t);
    }

    /// <summary>Starts collecting for one composite; everything collected before is dropped.</summary>
    public static void BeginCollect()
    {
        _overlayLines.Clear();
        _overlayQuads.Clear();
        _pendingFragments.Clear();
    }

    /// <summary>Whether the surface is being calibrated against its photo this frame, with a photo to project.</summary>
    public static bool ProjectsPhoto(Guid surfaceId)
    {
        return _photo.IsCurrent(ImGui.GetFrameCount())
               && _photo.Value.SurfaceId == surfaceId
               && _photo.Value.Srv is { IsDisposed: false };
    }

    /// <summary>
    /// Queues the photo discs for one mapping. Deferred until <see cref="CollectPhotoFragments"/>: a child
    /// region's content is composited after its parent, and would cover them.
    /// </summary>
    public static void DeferPhotoFragments(Surface surface, Surface.OutputMapping mapping, Matrix4x4 homography)
    {
        _pendingFragments.Add(new PendingFragment(surface, mapping, homography));
    }

    /// <summary>
    /// One disc of the straightened photo per reference point, warped through the pin like content would be.
    /// Centred on the point's *projection* — where the photo's feature lands — so with an over-determined pin
    /// the gap to the crosshair at its target is the miss, made visible.
    /// </summary>
    public static void CollectPhotoFragments(Int2 canvasResolution, List<OutputCompositor.DrawItem> drawItems)
    {
        if (_pendingFragments.Count == 0)
            return;

        // The overlay is drawn in canvas pixels, so the projection has to land there too.
        var canvasSize = new Vector2(Math.Max(1, canvasResolution.Width), Math.Max(1, canvasResolution.Height));
        var photo = _photo.Value;
        var radius = canvasResolution.Height * photo.RadiusOfHeight;

        foreach (var pending in _pendingFragments)
        {
            if (!SurfaceGeometry.TryGetSurfaceToOutput(pending.Surface, pending.Mapping, canvasSize, out var surfaceToOutput))
                continue;

            foreach (var annotation in pending.Surface.Annotations)
            {
                if (!annotation.IsPoint)
                    continue;

                // Around where the pin actually lands this point, which is the one thing the wall can show: the
                // mask cuts the warped photo at a canvas position, so cutting it at the mark would show whatever
                // the photo covers there rather than this point's own feature. The crosshair stays at the mark, so
                // the gap between the disc and its crosshair is the miss — drag until the disc covers the real
                // feature and the two come together.
                var centre = surfaceToOutput.TransformPoint(annotation.P1);
                drawItems.Add(new OutputCompositor.DrawItem(photo.Srv, pending.Homography, photo.Uv, Vector4.One,
                                                            Vector4.Zero, Vector4.Zero, Vector4.Zero,
                                                            new Vector4(centre.X, centre.Y, radius, 1)));
            }
        }
    }

    /// <summary>
    /// Carries a surface's annotation lines through its corner pin into output pixels. Warping here rather
    /// than in the shader is what keeps the projected line an even width — by the time it is drawn, no
    /// perspective is left in it.
    /// </summary>
    public static void CollectAnnotations(Surface surface, Surface.OutputMapping mapping, Vector2 canvasSize)
    {
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, canvasSize, out var surfaceToOutput))
            return;

        var frame = ImGui.GetFrameCount();

        // The frame check covers both staleness (the tool was disarmed) and an output being composited more
        // than once in a frame, where consuming the point would make it flicker.
        if (_aim.IsCurrent(frame) && _aim.Value.SurfaceId == surface.Id)
        {
            var aim = surfaceToOutput.TransformPoint(_aim.Value.InSurface);
            var arm = _aimCrosshairSize * 0.5f;
            var aimParams = new Vector4(_aimLineWidth, 0, 0, 0);
            var aimColor = UiColors.StatusAnimated.Rgba;
            _overlayLines.Add(new OverlayLine(new Vector4(aim.X - arm, aim.Y, aim.X + arm, aim.Y), aimColor, aimParams));
            _overlayLines.Add(new OverlayLine(new Vector4(aim.X, aim.Y - arm, aim.X, aim.Y + arm), aimColor, aimParams));
        }

        // Lines over a projected grid on a real wall are hard to pick out, so the endpoints pulse white and the
        // line being dragged thickens and pulses white ↔ its alignment colour — the readout you're aligning by.
        var blink = MathF.Sin((float)ImGui.GetTime() * _overlayBlinkRate) * 0.5f + 0.5f;
        var white = UiColors.ForegroundFull.Rgba;
        var emphasizedIndex = _emphasized.IsCurrent(frame) && _emphasized.Value.SurfaceId == surface.Id
                                  ? _emphasized.Value.Index
                                  : -1;

        for (var i = 0; i < surface.Annotations.Count; i++)
        {
            var annotation = surface.Annotations[i];
            var isEmphasizedPoint = i == emphasizedIndex;

            // A reference point is a crosshair to walk onto its feature; the one being dragged pulses. It
            // stands where it was placed on this output and stays there — the pin moving under it is the whole
            // signal, and a crosshair that rides the pin can never show it. An aimed one reads brighter.
            if (annotation.IsPoint)
            {
                // Stored as a fraction of the canvas; this overlay is drawn in its pixels. A point the editor
                // has not seeded yet falls back to the pin, which is where the seed would land anyway.
                var hasAim = mapping.PointAims.TryGetValue(annotation.Id, out var aim);
                var p = hasAim ? aim.Position * canvasSize : surfaceToOutput.TransformPoint(annotation.P1);

                var arm = (isEmphasizedPoint ? _pointCrosshairSize * 1.5f : _pointCrosshairSize) * 0.5f;
                var baseColor = hasAim && aim.IsAimed ? _pointColor : _pointColor * new Vector4(0.6f, 0.6f, 0.6f, 1);
                var pointColor = isEmphasizedPoint ? Vector4.Lerp(baseColor, white, blink) : baseColor;
                var pointWidth = new Vector4(isEmphasizedPoint ? _aimLineWidth * 2f : _aimLineWidth, 0, 0, 0);
                _overlayLines.Add(new OverlayLine(new Vector4(p.X - arm, p.Y, p.X + arm, p.Y), pointColor, pointWidth));
                _overlayLines.Add(new OverlayLine(new Vector4(p.X, p.Y - arm, p.X, p.Y + arm), pointColor, pointWidth));
                var ring = _annotationMarkerSize * 1.4f;
                _overlayQuads.Add(new OverlayQuad(new Vector4(p.X, p.Y, ring, ring), Vector4.Lerp(pointColor, white, blink), new Vector4(0, ring * 0.5f, 0, 0)));
                continue;
            }

            LineRectifier.IsHorizontal(annotation.P1, annotation.P2, out var deviation);
            var color = AlignmentColor(deviation).Rgba;
            var isEmphasized = i == emphasizedIndex;

            var a = surfaceToOutput.TransformPoint(annotation.P1);
            var b = surfaceToOutput.TransformPoint(annotation.P2);

            var lineColor = isEmphasized ? Vector4.Lerp(color, white, blink) : color;
            var lineWidth = isEmphasized ? _annotationLineWidth * 3f : _annotationLineWidth;
            _overlayLines.Add(new OverlayLine(new Vector4(a.X, a.Y, b.X, b.Y), lineColor, new Vector4(lineWidth, 0, 0, 0)));

            // The endpoints are what you actually aim at a feature: they pulse white to be findable, and the
            // dragged line's grow. Round for now — the slot a textured handle drops into later.
            var markerColor = Vector4.Lerp(color, white, blink);
            var markerSize = isEmphasized ? _annotationMarkerSize * 1.5f : _annotationMarkerSize;
            var markerShape = new Vector4(0, markerSize * 0.5f, 0, 0);
            _overlayQuads.Add(new OverlayQuad(new Vector4(a.X, a.Y, markerSize, markerSize), markerColor, markerShape));
            _overlayQuads.Add(new OverlayQuad(new Vector4(b.X, b.Y, markerSize, markerSize), markerColor, markerShape));
        }
    }

    /// <summary>
    /// The overlay passes, drawn last so they sit over the raster they are being aligned against. Both read a
    /// structured buffer of instances and expand six vertices each — no vertex buffer, no input layout. Expects
    /// the composite's target, topology and blend state to be bound already.
    /// </summary>
    public static void Draw(DeviceContext deviceContext, Int2 canvasResolution)
    {
        if (_overlayLines.Count == 0 && _overlayQuads.Count == 0)
            return;

        if (!EnsureShaders())
            return;

        _overlayParams.TargetSize = new Vector4(Math.Max(1, canvasResolution.Width), Math.Max(1, canvasResolution.Height), 0, 0);
        if (_overlayParamBuffer is not { IsDisposed: false })
        {
            _overlayParamBuffer = null;
            ResourceManager.SetupConstBuffer(_overlayParams, ref _overlayParamBuffer);
        }
        else
        {
            ResourceManager.UpdateConstBuffer(_overlayParams, _overlayParamBuffer);
        }
        deviceContext.VertexShader.SetConstantBuffer(0, _overlayParamBuffer);
        deviceContext.PixelShader.SetConstantBuffer(0, _overlayParamBuffer);

        if (TryUploadInstances(_overlayLines, ref _lineBuffer, ref _lineSrvSource, ref _lineSrv, out var lineCount))
        {
            deviceContext.VertexShader.Set(_lineVertexShader!.Value);
            deviceContext.PixelShader.Set(_linePixelShader!.Value);
            deviceContext.VertexShader.SetShaderResource(0, _lineSrv);
            deviceContext.Draw(lineCount * 6, 0);
        }

        if (TryUploadInstances(_overlayQuads, ref _quadBuffer, ref _quadSrvSource, ref _quadSrv, out var quadCount))
        {
            deviceContext.VertexShader.Set(_quadVertexShader!.Value);
            deviceContext.PixelShader.Set(_quadPixelShader!.Value);
            deviceContext.VertexShader.SetShaderResource(0, _quadSrv);
            deviceContext.Draw(quadCount * 6, 0);
        }

        deviceContext.VertexShader.SetShaderResource(0, null);
    }

    /// <summary>
    /// Uploads instances into a structured buffer straight from the list's memory. The buffer grows by doubling
    /// and never shrinks, so a count that changes from frame to frame (a line being dragged in and out of view)
    /// neither rebuilds it nor allocates; only the first <paramref name="count"/> elements are written and drawn.
    /// The view is re-created whenever the buffer was rebuilt.
    /// </summary>
    private static unsafe bool TryUploadInstances<T>(List<T> instances, ref Buffer? buffer, ref Buffer? viewSource,
                                                     ref ShaderResourceView? srv, out int count) where T : unmanaged
    {
        count = instances.Count;
        if (count == 0)
            return false;

        var stride = sizeof(T);
        var capacity = buffer is { IsDisposed: false } ? buffer.Description.SizeInBytes / stride : 0;
        if (count > capacity)
        {
            capacity = Math.Max(MinInstanceCapacity, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)count));
            if (buffer is { IsDisposed: true })
                buffer = null;

            ResourceManager.SetupStructuredBuffer(capacity * stride, stride, ref buffer);
        }

        if (buffer == null)
            return false;

        fixed (T* first = CollectionsMarshal.AsSpan(instances))
        {
            var region = new ResourceRegion(0, 0, 0, count * stride, 1, 1);
            ResourceManager.Device.ImmediateContext.UpdateSubresource(new SharpDX.DataBox((IntPtr)first, 0, 0), buffer, 0, region);
        }

        if (!ReferenceEquals(viewSource, buffer))
        {
            ResourceManager.CreateStructuredBufferSrv(buffer, ref srv);
            viewSource = buffer;
        }

        return srv is { IsDisposed: false };
    }

    private const int MinInstanceCapacity = 64;

    private static bool EnsureShaders()
    {
        _lineVertexShader ??= ResourceManager.CreateShaderResource<VertexShader>(LineShaderPath, null, () => "vsMain");
        _linePixelShader ??= ResourceManager.CreateShaderResource<PixelShader>(LineShaderPath, null, () => "psMain");
        _quadVertexShader ??= ResourceManager.CreateShaderResource<VertexShader>(QuadShaderPath, null, () => "vsMain");
        _quadPixelShader ??= ResourceManager.CreateShaderResource<PixelShader>(QuadShaderPath, null, () => "psMain");
        return _lineVertexShader.Value != null && _linePixelShader.Value != null
               && _quadVertexShader.Value != null && _quadPixelShader.Value != null;
    }

    /// <summary>
    /// A value the views re-state every frame it applies. It stays current for one frame past the last
    /// statement, which also covers an output composited more than once in a frame.
    /// </summary>
    private struct FrameStamped<T> where T : struct
    {
        public T Value;
        public int Frame;

        public void Set(T value, int frame)
        {
            Value = value;
            Frame = frame;
        }

        public readonly bool IsCurrent(int frame) => frame - Frame <= 1;
    }

    private readonly record struct AimPoint(Guid SurfaceId, Vector2 InSurface);

    private readonly record struct CalibrationPhoto(Guid SurfaceId, ShaderResourceView Srv, Vector4 Uv, float RadiusOfHeight);

    private readonly record struct EmphasizedAnnotation(Guid SurfaceId, int Index);

    private readonly record struct PendingFragment(Surface Surface, Surface.OutputMapping Mapping, Matrix4x4 Homography);

    // Overlay instances. Every member is a float4 so the C# layout and the HLSL structured-buffer packing
    // rules cannot disagree, and the spare lanes leave room to grow (texture slot, dash pattern).
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct OverlayLine(Vector4 Points, Vector4 Color, Vector4 Params);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly record struct OverlayQuad(Vector4 Rect, Vector4 Color, Vector4 Params);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct OverlayShaderParams
    {
        public Vector4 TargetSize;
    }

    private const string LineShaderPath = "Lib:shaders/dx11/output-lines.hlsl";
    private const string QuadShaderPath = "Lib:shaders/dx11/output-quads.hlsl";
    private const float _annotationLineWidth = 2.5f;
    private const float _annotationMarkerSize = 11f;
    private const float _aimCrosshairSize = 60f;
    private const float _pointCrosshairSize = 40f;
    private const float _aimLineWidth = 1.5f;
    private const float _overlayBlinkRate = 8f; // matches the editor-canvas handles so the two stay in phase
    private static readonly Vector4 _pointColor = new(0.45f, 0.95f, 0.55f, 1); // the surface green, bright enough for a wall

    // Frame -10: nothing is current until a view states it.
    private static FrameStamped<AimPoint> _aim = new() { Frame = -10 };
    private static FrameStamped<CalibrationPhoto> _photo = new() { Frame = -10 };
    private static FrameStamped<EmphasizedAnnotation> _emphasized = new() { Frame = -10 };

    private static readonly List<PendingFragment> _pendingFragments = [];
    private static readonly List<OverlayLine> _overlayLines = [];
    private static readonly List<OverlayQuad> _overlayQuads = [];
    private static Resource<VertexShader>? _lineVertexShader;
    private static Resource<PixelShader>? _linePixelShader;
    private static Resource<VertexShader>? _quadVertexShader;
    private static Resource<PixelShader>? _quadPixelShader;
    private static OverlayShaderParams _overlayParams;
    private static Buffer? _overlayParamBuffer;
    private static Buffer? _lineBuffer;
    private static Buffer? _lineSrvSource;
    private static ShaderResourceView? _lineSrv;
    private static Buffer? _quadBuffer;
    private static Buffer? _quadSrvSource;
    private static ShaderResourceView? _quadSrv;
}
