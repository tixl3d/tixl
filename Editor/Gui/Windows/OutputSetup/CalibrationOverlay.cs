#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;
using SharpDX.Direct3D11;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction.CanvasEditing;
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
    /// Hands the compositor its calibration hook. Installed once at startup: a host that never calls this
    /// composites without any of it, which is what a show wants.
    /// </summary>
    public static void Install()
    {
        OutputCompositor.Overlay = new Hook();
    }

    /// <summary>Forwards to the static entry points, which hold the session state the tools write into.</summary>
    private sealed class Hook : ICalibrationOverlay
    {
        void ICalibrationOverlay.BeginCollect() => CalibrationOverlay.BeginCollect();
        bool ICalibrationOverlay.ProjectsPhoto(Guid surfaceId) => CalibrationOverlay.ProjectsPhoto(surfaceId);

        void ICalibrationOverlay.DeferPhotoFragments(Surface surface, Surface.OutputMapping mapping, Matrix4x4 homography)
            => CalibrationOverlay.DeferPhotoFragments(surface, mapping, homography);

        void ICalibrationOverlay.CollectPhotoFragments(Int2 canvasResolution, List<OutputCompositor.DrawItem> drawItems)
            => CalibrationOverlay.CollectPhotoFragments(canvasResolution, drawItems);

        void ICalibrationOverlay.CollectAnnotations(Surface surface, Surface.OutputMapping mapping, Vector2 canvasSize)
            => CalibrationOverlay.CollectAnnotations(surface, mapping, canvasSize);

        void ICalibrationOverlay.Draw(DeviceContext deviceContext, Int2 canvasResolution)
            => CalibrationOverlay.Draw(deviceContext, canvasResolution);
    }

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
    /// against the real wall. The outline of <paramref name="activePointId"/> blinks, so it can be found there.
    /// The points belong to <paramref name="pointSource"/>, which for a region is its traced ancestor;
    /// <paramref name="pointOffset"/> carries them into the pinned surface's space.
    /// </summary>
    public static void SetCalibrationPhoto(Guid surfaceId, ShaderResourceView srv, Vector2 uvMin, Vector2 uvMax, float radiusOfHeight, Guid activePointId,
                                           Surface pointSource, Vector2 pointOffset)
    {
        var uv = new Vector4(uvMin.X, uvMin.Y, uvMax.X, uvMax.Y);
        _photo.Set(new CalibrationPhoto(surfaceId, srv, uv, radiusOfHeight, activePointId, pointSource, pointOffset), ImGui.GetFrameCount());
    }

    /// <summary>Shows an arrow around the active point in a direction on the canvas (Y down), while one is being chosen.</summary>
    public static void SetUpDirectionPreview(Guid surfaceId, Vector2 direction)
    {
        _upDirectionPreview.Set(new UpDirectionPreview(surfaceId, direction), ImGui.GetFrameCount());
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
    /// One disc of the straightened photo per reference point, warped through the pin like content would be and
    /// centred on the point's target. It stays under the cursor and stands still while other points are dragged,
    /// so it can be walked onto the real feature while watching the wall.
    /// </summary>
    public static void CollectPhotoFragments(Int2 canvasResolution, List<OutputCompositor.DrawItem> drawItems)
    {
        if (_pendingFragments.Count == 0)
            return;

        // The overlay is drawn in canvas pixels, so the projection has to land there too.
        var canvasSize = new Vector2(Math.Max(1, canvasResolution.Width), Math.Max(1, canvasResolution.Height));
        var photo = _photo.Value;
        var radius = canvasResolution.Height * photo.RadiusOfHeight;

        // The photo's whole texture is drawn rather than the surface's window, so a disc at the surface's edge isn't
        // cut there: the pin's unit square is stretched to where the texture's corners lie in it.
        var uvSpan = new Vector2(MathF.Max(photo.Uv.Z - photo.Uv.X, 0.0001f), MathF.Max(photo.Uv.W - photo.Uv.Y, 0.0001f));
        var textureToPinUnit = Matrix4x4.CreateScale(1 / uvSpan.X, 1 / uvSpan.Y, 1)
                               * Matrix4x4.CreateTranslation(-photo.Uv.X / uvSpan.X, -photo.Uv.Y / uvSpan.Y, 0);

        foreach (var pending in _pendingFragments)
        {
            if (!SurfaceGeometry.TryGetSurfaceToOutput(pending.Surface, pending.Mapping, canvasSize, out var surfaceToOutput))
                continue;

            SurfaceGeometry.LocalBounds(pending.Surface, out var surfaceMin, out var surfaceMax);
            var surfaceSpan = Vector2.Max(surfaceMax - surfaceMin, new Vector2(0.0001f));
            var isPinWarpable = IsPinWarpable(pending.Mapping, surfaceToOutput, surfaceMin, surfaceSpan, photo.Uv, uvSpan, canvasSize);

            foreach (var annotation in photo.PointSource.Annotations)
            {
                if (!annotation.IsPoint)
                    continue;

                var pointInSurface = annotation.P1 + photo.PointOffset;
                var projected = surfaceToOutput.TransformPoint(pointInSurface);
                var centre = pending.Mapping.PointAims.TryGetValue(annotation.Id, out var aim) ? aim.Position * canvasSize : projected;

                Matrix4x4 textureToNdc;
                if (isPinWarpable)
                {
                    // The warped photo is shifted, not only masked at the target: a mask alone would show whatever
                    // the photo covers there rather than this point's own feature.
                    var shift = (centre - projected) / canvasSize * 2;
                    textureToNdc = textureToPinUnit * pending.Homography * Matrix4x4.CreateTranslation(shift.X, -shift.Y, 0);
                }
                else
                {
                    // Upright and a fixed size: wrong about the keystone and right about what the feature looks
                    // like. A collapsed pin is the state you are most likely to be in while repairing it.
                    var inSurface = (pointInSurface - surfaceMin) / surfaceSpan;
                    var featureUv = new Vector2(photo.Uv.X + uvSpan.X * inSurface.X, photo.Uv.Y + uvSpan.Y * (1 - inSurface.Y));
                    var pixelsPerV = 1 / MathF.Max(uvSpan.Y * photo.RadiusOfHeight, 0.0001f) * radius;
                    var pixelsPerU = pixelsPerV * (surfaceSpan.X / uvSpan.X) / (surfaceSpan.Y / uvSpan.Y);
                    var textureOrigin = centre - featureUv * new Vector2(pixelsPerU, pixelsPerV);
                    textureToNdc = new Matrix4x4
                                       {
                                           M11 = pixelsPerU / canvasSize.X * 2,
                                           M22 = -pixelsPerV / canvasSize.Y * 2,
                                           M33 = 1,
                                           M41 = textureOrigin.X / canvasSize.X * 2 - 1,
                                           M42 = 1 - textureOrigin.Y / canvasSize.Y * 2,
                                           M44 = 1,
                                       };
                }

                drawItems.Add(new OutputCompositor.DrawItem(photo.Srv, textureToNdc, new Vector4(0, 0, 1, 1), Vector4.One,
                                                            Vector4.Zero, Vector4.Zero, Vector4.Zero,
                                                            new Vector4(centre.X, centre.Y, radius, 1)));
            }
        }
    }

    /// <summary>
    /// Whether the photo can be warped through the pin at all. A collapsed pin smears every disc into one colour,
    /// and a steep one throws the corners of the photo's surround towards infinity.
    /// </summary>
    private static bool IsPinWarpable(Surface.OutputMapping mapping, in Homography surfaceToOutput, Vector2 surfaceMin, Vector2 surfaceSpan,
                                      Vector4 uv, Vector2 uvSpan, Vector2 canvasSize)
    {
        Span<Vector2> corners = stackalloc Vector2[4];
        for (var c = 0; c < 4; c++)
            corners[c] = mapping.Quad[c] * canvasSize;

        CanvasDraw.Bounds(corners, out var pinMin, out var pinMax);
        var pinSize = pinMax - pinMin;

        for (var c = 0; c < 4; c++)
        {
            var textureCorner = new Vector2(c is 1 or 2 ? 1 : 0, c is 2 or 3 ? 1 : 0);
            var inSurface = (textureCorner - new Vector2(uv.X, uv.Y)) / uvSpan;
            corners[c] = surfaceToOutput.TransformPoint(new Vector2(surfaceMin.X + inSurface.X * surfaceSpan.X,
                                                                    surfaceMin.Y + (1 - inSurface.Y) * surfaceSpan.Y));
        }

        CanvasDraw.Bounds(corners, out var photoMin, out var photoMax);
        var photoSize = photoMax - photoMin;
        return float.IsFinite(photoSize.X) && float.IsFinite(photoSize.Y)
               && pinSize.X >= MinWarpablePinExtent && pinSize.Y >= MinWarpablePinExtent
               && photoSize.X <= pinSize.X * MaxPhotoGrowthThroughPin && photoSize.Y <= pinSize.Y * MaxPhotoGrowthThroughPin;
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

        if (ProjectsPhoto(surface.Id))
        {
            var photo = _photo.Value;
            foreach (var annotation in photo.PointSource.Annotations)
            {
                if (annotation.IsPoint)
                    CollectPoint(annotation.Id, annotation.P1 + photo.PointOffset, mapping, surfaceToOutput, canvasSize, blink);
            }
        }

        for (var i = 0; i < surface.Annotations.Count; i++)
        {
            var annotation = surface.Annotations[i];
            if (annotation.IsPoint)
                continue;

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
    /// The marks around a reference point's disc: an outline that says whether the point is set, a blinking one
    /// on the active point, a crosshair on the target, and an arrow to where the solved pin lands the point.
    /// </summary>
    private static void CollectPoint(Guid pointId, Vector2 pointInSurface, Surface.OutputMapping mapping, in Homography surfaceToOutput, Vector2 canvasSize,
                                     float blink)
    {
        // Stored as a fraction of the canvas; this overlay is drawn in its pixels. A point the editor has not
        // seeded yet falls back to the pin, which is where the seed would land anyway.
        var projected = surfaceToOutput.TransformPoint(pointInSurface);
        var hasAim = mapping.PointAims.TryGetValue(pointId, out var aim);
        var target = hasAim ? aim.Position * canvasSize : projected;
        var isSet = hasAim && aim.IsAimed;
        var photo = _photo.Value;

        var discRadius = canvasSize.Y * photo.RadiusOfHeight;
        var outlineWidth = isSet ? _setPointOutlineWidth : _pointOutlineWidth;
        AddRing(target, discRadius - _outlineOverlapWithDisc, outlineWidth + _outlineOverlapWithDisc, isSet ? _setPointOutlineColor : _pointOutlineColor);

        if (pointId == photo.ActivePointId)
        {
            var white = UiColors.ForegroundFull.Rgba;
            AddRing(target, discRadius + outlineWidth, _activePointOutlineWidth, new Vector4(white.X, white.Y, white.Z, blink));

            if (_upDirectionPreview.IsCurrent(ImGui.GetFrameCount()) && _upDirectionPreview.Value.SurfaceId == photo.SurfaceId)
                AddArrow(target, target + _upDirectionPreview.Value.Direction * (discRadius + _upArrowReach), UiColors.StatusAnimated.Rgba);
        }

        var arm = _pointCrosshairSize * 0.5f;
        var crosshairColor = isSet ? _pointColor : _pointColor * new Vector4(0.6f, 0.6f, 0.6f, 1);
        var crosshairWidth = new Vector4(_aimLineWidth, 0, 0, 0);
        _overlayLines.Add(new OverlayLine(new Vector4(target.X - arm, target.Y, target.X + arm, target.Y), crosshairColor, crosshairWidth));
        _overlayLines.Add(new OverlayLine(new Vector4(target.X, target.Y - arm, target.X, target.Y + arm), crosshairColor, crosshairWidth));

        if ((projected - target).Length() >= _minVisibleMissPx)
            AddArrow(target, projected, UiColors.StatusAttention.Rgba);
    }

    private static void AddArrow(Vector2 from, Vector2 to, Vector4 color)
    {
        var direction = Vector2.Normalize(to - from);
        var side = new Vector2(-direction.Y, direction.X) * _arrowHeadSize * 0.5f;
        var headBase = to - direction * _arrowHeadSize;
        var width = new Vector4(_annotationLineWidth, 0, 0, 0);
        _overlayLines.Add(new OverlayLine(new Vector4(from.X, from.Y, to.X, to.Y), color, width));
        _overlayLines.Add(new OverlayLine(new Vector4(to.X, to.Y, headBase.X + side.X, headBase.Y + side.Y), color, width));
        _overlayLines.Add(new OverlayLine(new Vector4(to.X, to.Y, headBase.X - side.X, headBase.Y - side.Y), color, width));
    }

    /** A ring whose inner edge sits at <paramref name="innerRadius"/>. */
    private static void AddRing(Vector2 centre, float innerRadius, float width, Vector4 color)
    {
        var size = (innerRadius + width) * 2;
        _overlayQuads.Add(new OverlayQuad(new Vector4(centre.X, centre.Y, size, size), color, new Vector4(0, size * 0.5f, width, 0)));
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

    private readonly record struct CalibrationPhoto(Guid SurfaceId, ShaderResourceView Srv, Vector4 Uv, float RadiusOfHeight, Guid ActivePointId,
                                                    Surface PointSource, Vector2 PointOffset);

    private readonly record struct EmphasizedAnnotation(Guid SurfaceId, int Index);

    private readonly record struct UpDirectionPreview(Guid SurfaceId, Vector2 Direction);

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
    private const float _pointOutlineWidth = 2.5f;
    private const float _setPointOutlineWidth = 3f;
    private const float _activePointOutlineWidth = 6f;

    /** Reaches under the disc's soft edge (2 px in corner-pin-layer.hlsl) and the quad shader's 1 px of slack, so no gap shows. */
    private const float _outlineOverlapWithDisc = 3f;
    private const float _minVisibleMissPx = 6f;
    private const float _arrowHeadSize = 14f;
    private const float _upArrowReach = 60f;
    private const float _aimLineWidth = 1.5f;
    private const float MinWarpablePinExtent = 8f;
    private const float MaxPhotoGrowthThroughPin = 8f;
    private static readonly Vector4 _pointOutlineColor = new(0.25f, 0.25f, 0.25f, 1);
    private static readonly Vector4 _setPointOutlineColor = new(1, 1, 1, 1); // black would vanish on an unlit wall
    private const float _overlayBlinkRate = 8f; // matches the editor-canvas handles so the two stay in phase
    private static readonly Vector4 _pointColor = new(0.45f, 0.95f, 0.55f, 1); // the surface green, bright enough for a wall

    // Frame -10: nothing is current until a view states it.
    private static FrameStamped<AimPoint> _aim = new() { Frame = -10 };
    private static FrameStamped<CalibrationPhoto> _photo = new() { Frame = -10 };
    private static FrameStamped<EmphasizedAnnotation> _emphasized = new() { Frame = -10 };
    private static FrameStamped<UpDirectionPreview> _upDirectionPreview = new() { Frame = -10 };

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
