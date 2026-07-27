#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Output;
using T3.Core.Rendering;
using T3.Core.Resource;
using T3.Editor.App;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel.ProjectHandling;
using Buffer = SharpDX.Direct3D11.Buffer;
using Format = SharpDX.DXGI.Format;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Int2 = T3.Core.DataTypes.Vector.Int2;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Composites the content bound to a setup output. Walking the active setup's surfaces, it pulls each
/// surface's content from the registered <see cref="IOutputSink"/> (SendToOutput), corner-pin warps the
/// surface's source slice into the output's own render target, and returns the composite texture. The
/// sinks never draw — the drawing lives here, in one place. Content is pulled once per sink (cached), so
/// several surfaces slicing one image cost a single upstream evaluation.
/// </summary>
internal static class OutputManager
{
    /// <summary>The output currently presented on the secondary display window (Guid.Empty = none).</summary>
    public static Guid PresentedOutputId;

    /// <summary>
    /// Per-frame driver: renders each display-bound output's composite (so its content evaluates even
    /// when nothing displays it) and presents it on its bound display. A binding is the intent to
    /// present, so this auto-resumes a persisted binding after a restart. Skipped while the second
    /// view mirrors the editor UI. Call before the viewer's back buffer is bound for the frame.
    /// </summary>
    public static void UpdatePresentation()
    {
        if (UserSettings.Config.MirrorUiOnSecondView)
            return;

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
            return;

        OutputDefinition? boundOutput = null;
        DeviceBinding? binding = null;
        foreach (var output in setup.Outputs)
        {
            // Send=false pauses presenting this output without dropping its binding.
            if (!output.Send)
                continue;

            var candidate = machineConfig.TryGetBinding(output.Id);
            if (candidate == null)
                continue;

            boundOutput = output;
            binding = candidate;
            break;
        }

        if (boundOutput == null || binding == null)
        {
            // Nothing presentable (unbound, or Send paused) — take down the second window if it was up.
            if (PresentedOutputId != Guid.Empty)
            {
                WindowManager.ShowSecondaryRenderWindow = false;
                PresentedOutputId = Guid.Empty;
                _presentedDisplayIndex = -1;
            }

            return;
        }

        PresentedOutputId = boundOutput.Id;

        // RenderOutput returns null when there's nothing to composite (no active sink for this
        // output, empty target list, paused update). Assigning null trips the Texture2D→SharpDX
        // implicit conversion (dereferences TextureObject) — keep the last presented frame instead.
        var composite = RenderOutput(boundOutput.Id);
        if (composite != null)
            ProgramWindows.Viewer.Texture = composite;

        if (!WindowManager.ShowSecondaryRenderWindow || _presentedDisplayIndex != binding.DisplayIndex)
        {
            WindowManager.ShowSecondaryRenderWindow = true;
            ProgramWindows.Viewer.SetFullScreen(binding.DisplayIndex);
            _presentedDisplayIndex = binding.DisplayIndex;
        }
    }

    /// <summary>
    /// A representative source-content texture for the output — the first surface's content. Used as the
    /// backdrop of the content-slice editor. (Shared-content case; surfaces bound to different content
    /// still resolve their own slices, they just share this one backdrop.)
    /// </summary>
    public static Texture2D? TryGetOutputContent(Guid outputId)
    {
        var setup = ActiveSetup.Current;
        var output = ActiveSetup.TryFindOutput(outputId);
        if (setup == null || output == null)
            return null;

        _context ??= new EvaluationContext();
        _context.Reset();
        _context.RequestedResolution = output.CanvasResolution;

        InvalidateContentOncePerFrame(_context);

        foreach (var surface in setup.Surfaces)
        {
            if (!TryResolveSurfaceContent(setup, surface, out var surfaceSink, out _) || surfaceSink == null)
                continue;

            var content = surfaceSink.GetContent(_context);
            if (content is { IsDisposed: false })
                return content;
        }

        if (!TryResolveSliceContent(setup, output.SliceId, out var directSink, out _) || directSink == null)
            return null;

        var direct = directSink.GetContent(_context);
        return direct is { IsDisposed: false } ? direct : null;
    }

    /// <summary>
    /// Content is pulled manually, outside the normal output path, so the same graph invalidation must run —
    /// but only once per frame: presentation, the setup canvas, and the content preview can all pull in one
    /// frame, and every extra tick re-evaluates each sink's whole upstream graph.
    /// </summary>
    private static void InvalidateContentOncePerFrame(EvaluationContext context)
    {
        var frame = ImGui.GetFrameCount();
        if (frame == _invalidatedContentFrame)
            return;

        _invalidatedContentFrame = frame;

        DirtyFlag.GlobalInvalidationTick++;
        foreach (var sink in OutputSinkRegistry.Sinks)
        {
            // Update=false freezes this content at its last frame — skip its invalidation.
            if (sink.GetUpdateEnabled(context))
                sink.InvalidateContent();
        }
    }

    /// <summary>Renders the output's composite, or null if nothing is bound to it.</summary>
    public static Texture2D? RenderOutput(Guid outputId)
    {
        var setup = ActiveSetup.Current;
        var output = ActiveSetup.TryFindOutput(outputId);
        if (setup == null || output == null)
            return null;

        _context ??= new EvaluationContext();
        _context.Reset();
        _context.RequestedResolution = output.CanvasResolution;

        InvalidateContentOncePerFrame(_context);

        // Phase 1: resolve each surface's content and mapping. Pulling content here (before our RT is
        // bound) keeps the content's own rendering from clobbering the target we bind in phase 2.
        _drawItems.Clear();
        _overlayLines.Clear();
        _overlayQuads.Clear();
        foreach (var surface in setup.Surfaces)
        {
            if (!surface.Render)
                continue;

            // A Layout child carries no corner pin of its own — it rides its parent's, so the mappings to walk
            // (and the quad each one yields) come from the parent.
            // Regions nest arbitrarily deep, so walk up to whichever ancestor actually holds the pin.
            var carrier = surface;
            if (surface.Kind == T3.Core.Output.Surface.SurfaceKinds.Layout && surface.ParentId != Guid.Empty)
            {
                carrier = SurfaceGeometry.FindCarrier(setup, surface.Id, outputId);
                if (carrier == null || !carrier.Render)
                    continue;
            }

            TryResolveSurfaceContent(setup, surface, out var sink, out var resolvedRect);
            var content = sink?.GetContent(_context);
            var srv = content is { IsDisposed: false } ? SrvManager.GetSrvForTexture(content) : null;
            var hasContent = srv is { IsDisposed: false };
            var color = hasContent ? sink!.GetColor(_context) : Vector4.One;
            var sourceRect = hasContent ? resolvedRect : _fullSourceRect;

            // Metres spanned by the surface, and the origin (its anchor) in source UV — the pivot is
            // normalized from the bottom-left while V runs downward.
            var metres = new Vector2(Math.Clamp(surface.SizeInMeters.X, 0.01f, 1000f),
                                     Math.Clamp(surface.SizeInMeters.Y, 0.01f, 1000f));
            var pivot = surface.Placement?.Pivot ?? Vector2.Zero;
            var gridOrigin = new Vector4(pivot.X, 1f - pivot.Y,
                                         Math.Clamp(surface.GridSubdivisions, 1, 100), _gridMinorOpacity);

            foreach (var mapping in carrier.OutputMappings)
            {
                if (mapping.OutputId != outputId)
                    continue;

                var quad = mapping.Quad;
                if (!ReferenceEquals(carrier, surface))
                {
                    // Buffer is consumed by TryComputeNdcHomography before the next iteration reuses it.
                    if (!SurfaceGeometry.TryGetChildQuad(setup, carrier, surface, mapping, _childQuadBuffer))
                        continue;

                    quad = _childQuadBuffer;
                }

                if (!TryComputeNdcHomography(quad, output.CanvasResolution, out var homography))
                    continue;

                if (hasContent)
                    _drawItems.Add(new DrawItem(srv, homography, sourceRect, color, Vector4.Zero, Vector4.Zero, Vector4.Zero));

                // Calibration raster after the content, so it composites *over* it and stays readable while
                // aligning. Emitted with or without content — with none, it's lines on the cleared black.
                if (surface.ShowGrid)
                {
                    _drawItems.Add(new DrawItem(null, homography, _fullSourceRect, Vector4.One,
                                                new Vector4(metres.X, metres.Y, _gridLineThickness, 1), _gridColor, gridOrigin));

                    // Annotation lines have to reach the wall to be usable at all: you align one by nudging
                    // it until its *projection* lies along a real feature. They ride the same switch as the
                    // raster — same calibration session, and neither belongs in a show.
                    if (ReferenceEquals(carrier, surface))
                        CollectAnnotationOverlay(surface, mapping);
                }
            }
        }

        // The output can also name a slice directly, shown full-frame (Shape 2: the content was rendered
        // through the projector camera, so it already maps 1:1 to the output; no corner-pin warp).
        if (_drawItems.Count == 0 && TryResolveSliceContent(setup, output.SliceId, out var directSink, out var directRect))
        {
            var content = directSink!.GetContent(_context);
            if (content is { IsDisposed: false })
            {
                var srv = SrvManager.GetSrvForTexture(content);
                if (srv is { IsDisposed: false })
                    _drawItems.Add(new DrawItem(srv, _fullscreenNdc.ToMatrix4x4(), directRect, directSink.GetColor(_context),
                                                Vector4.Zero, Vector4.Zero, Vector4.Zero));
            }
        }

        if (_drawItems.Count == 0)
            return null;

        var target = GetOrCreateTarget(outputId, output.CanvasResolution);
        if (target == null)
            return null;

        if (!EnsureShaders())
            return null;

        var vs = _vertexShaderResource!.Value;
        var ps = _pixelShaderResource!.Value;

        // Phase 2: bind our render target and composite. No state restore — like the thumbnail renderer,
        // this runs during ImGui layout and ImGui rebinds the main target when it renders at frame end.
        var deviceContext = ResourceManager.Device.ImmediateContext;
        deviceContext.OutputMerger.SetTargets(target.Rtv);
        deviceContext.Rasterizer.SetViewport(new ViewportF(0, 0, target.Size.Width, target.Size.Height, 0f, 1f));
        // Opaque black: uncovered output area is "no projection", and the editor preview shouldn't show the
        // panel gray through a transparent composite.
        deviceContext.ClearRenderTargetView(target.Rtv, new RawColor4(0, 0, 0, 1));

        deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        deviceContext.InputAssembler.InputLayout = null;
        deviceContext.VertexShader.Set(vs);
        deviceContext.GeometryShader.Set(null);
        deviceContext.PixelShader.Set(ps);
        deviceContext.PixelShader.SetSampler(0, LinearSampler);
        deviceContext.Rasterizer.State = CullNoneRasterizerState;
        deviceContext.OutputMerger.BlendState = DefaultRenderingStates.DefaultBlendState;
        deviceContext.OutputMerger.DepthStencilState = DefaultRenderingStates.DisabledDepthStencilState;

        foreach (var item in _drawItems)
        {
            _shaderParams.Homography = item.Homography;
            _shaderParams.Color = item.Color;
            _shaderParams.GridParams = item.GridParams;
            _shaderParams.GridColor = item.GridColor;
            _shaderParams.GridOrigin = item.GridOrigin;
            SetSourceRect(item.SourceRect);
            ResourceManager.SetupConstBuffer(_shaderParams, ref _paramBuffer);
            deviceContext.VertexShader.SetConstantBuffer(0, _paramBuffer);
            deviceContext.PixelShader.SetConstantBuffer(0, _paramBuffer);
            deviceContext.PixelShader.SetShaderResource(0, item.Srv);
            deviceContext.Draw(6, 0);
        }

        deviceContext.PixelShader.SetShaderResource(0, null);

        DrawOverlay(deviceContext, output.CanvasResolution);
        return target.Texture;
    }

    /// <summary>
    /// Where a tool is currently being aimed on <paramref name="surfaceId"/>, in surface meters. Projected as
    /// a crosshair so the point can be placed against a physical feature *before* the drag starts — until the
    /// first press there is nothing else on the wall to aim with. Re-stated every frame the tool is armed;
    /// it expires on its own once that stops.
    /// </summary>
    public static void SetAimPoint(Guid surfaceId, Vector2 inSurface)
    {
        _aimSurfaceId = surfaceId;
        _aimInSurface = inSurface;
        _aimFrame = ImGuiNET.ImGui.GetFrameCount();
    }

    /// <summary>
    /// Marks one of a surface's annotation lines as the one being edited, so the projected composite draws it
    /// thick and pulsing on the wall (the whole point of calibrating is watching that projection). Re-stated
    /// each drag frame; expires on its own like the aim point.
    /// </summary>
    public static void EmphasizeAnnotation(Guid surfaceId, int index)
    {
        _emphasizedSurfaceId = surfaceId;
        _emphasizedIndex = index;
        _emphasizedFrame = ImGuiNET.ImGui.GetFrameCount();
    }

    /// <summary>
    /// Carries a surface's annotation lines through its corner pin into output pixels. Warping here rather
    /// than in the shader is what keeps the projected line an even width — by the time it is drawn, no
    /// perspective is left in it.
    /// </summary>
    private static void CollectAnnotationOverlay(T3.Core.Output.Surface surface, T3.Core.Output.Surface.OutputMapping mapping)
    {
        if (!SurfaceGeometry.TryGetSurfaceToOutput(surface, mapping, out var surfaceToOutput))
            return;

        // The frame check covers both staleness (the tool was disarmed) and an output being composited more
        // than once in a frame, where consuming the point would make it flicker.
        if (_aimSurfaceId == surface.Id && ImGuiNET.ImGui.GetFrameCount() - _aimFrame <= 1)
        {
            var aim = surfaceToOutput.TransformPoint(_aimInSurface);
            var arm = _aimCrosshairSize * 0.5f;
            var aimParams = new Vector4(_aimLineWidth, 0, 0, 0);
            var aimColor = T3.Editor.Gui.Styling.UiColors.StatusAnimated.Rgba;
            _overlayLines.Add(new OverlayLine(new Vector4(aim.X - arm, aim.Y, aim.X + arm, aim.Y), aimColor, aimParams));
            _overlayLines.Add(new OverlayLine(new Vector4(aim.X, aim.Y - arm, aim.X, aim.Y + arm), aimColor, aimParams));
        }

        // Lines over a projected grid on a real wall are hard to pick out, so the endpoints pulse white and the
        // line being dragged thickens and pulses white ↔ its alignment colour — the readout you're aligning by.
        var blink = MathF.Sin((float)ImGuiNET.ImGui.GetTime() * _overlayBlinkRate) * 0.5f + 0.5f;
        var white = T3.Editor.Gui.Styling.UiColors.ForegroundFull.Rgba;
        var emphasizedIndex = _emphasizedSurfaceId == surface.Id && ImGuiNET.ImGui.GetFrameCount() - _emphasizedFrame <= 1
                                  ? _emphasizedIndex
                                  : -1;

        for (var i = 0; i < surface.Annotations.Count; i++)
        {
            var annotation = surface.Annotations[i];
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
    /// structured buffer of instances and expand six vertices each — no vertex buffer, no input layout.
    /// </summary>
    private static void DrawOverlay(DeviceContext deviceContext, Int2 canvasResolution)
    {
        if (_overlayLines.Count == 0 && _overlayQuads.Count == 0)
            return;

        if (!EnsureOverlayShaders())
            return;

        _overlayParams.TargetSize = new Vector4(Math.Max(1, canvasResolution.Width), Math.Max(1, canvasResolution.Height), 0, 0);
        ResourceManager.SetupConstBuffer(_overlayParams, ref _overlayParamBuffer);
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
    /// Uploads instances into a structured buffer, re-creating the view when the buffer had to be rebuilt —
    /// which <see cref="ResourceManager.SetupStructuredBuffer{T}"/> does whenever the count changes, leaving
    /// any earlier view pointing at a disposed buffer.
    /// </summary>
    private static bool TryUploadInstances<T>(List<T> instances, ref Buffer? buffer, ref Buffer? viewSource,
                                              ref ShaderResourceView? srv, out int count) where T : struct
    {
        count = instances.Count;
        if (count == 0)
            return false;

        var stride = System.Runtime.InteropServices.Marshal.SizeOf<T>();
        using (var data = new SharpDX.DataStream(stride * count, true, true))
        {
            // Written one at a time: the span overload would want an array, and materializing one here would
            // allocate every frame the overlay is visible.
            foreach (var instance in instances)
                data.Write(instance);

            data.Position = 0;
            ResourceManager.SetupStructuredBuffer(data, stride * count, stride, ref buffer);
        }

        if (buffer == null)
            return false;

        if (!ReferenceEquals(viewSource, buffer))
        {
            ResourceManager.CreateStructuredBufferSrv(buffer, ref srv);
            viewSource = buffer;
        }

        return srv is { IsDisposed: false };
    }

    private static bool EnsureOverlayShaders()
    {
        _lineVertexShader ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.VertexShader>(LineShaderPath, null, () => "vsMain");
        _linePixelShader ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.PixelShader>(LineShaderPath, null, () => "psMain");
        _quadVertexShader ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.VertexShader>(QuadShaderPath, null, () => "vsMain");
        _quadPixelShader ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.PixelShader>(QuadShaderPath, null, () => "psMain");
        return _lineVertexShader.Value != null && _linePixelShader.Value != null
               && _quadVertexShader.Value != null && _quadPixelShader.Value != null;
    }

    /// <summary>Aligned to within a fraction of a degree reads as good; off by a few, as work left to do.</summary>
    public static T3.Core.DataTypes.Vector.Color AlignmentColor(float deviationInDegrees)
    {
        var t = Math.Clamp((deviationInDegrees - LineRectifier.AlignedDegrees)
                           / (LineRectifier.MisalignedDegrees - LineRectifier.AlignedDegrees), 0f, 1f);
        return T3.Core.DataTypes.Vector.Color.MixOkLab(T3.Editor.Gui.Styling.UiColors.StatusOkay,
                                                       T3.Editor.Gui.Styling.UiColors.StatusWarning, t);
    }

    /// <summary>
    /// Warps a texture (sampled full) into a scratch render target so its corners land on
    /// <paramref name="destQuad"/> (in <paramref name="targetSize"/> pixels). Used by the reference-image
    /// straighten transition; returns the warped texture (reused across calls) or null.
    /// </summary>
    public static Texture2D? RenderWarpedTexture(Texture2D? source, Vector2[] destQuad, Int2 targetSize)
    {
        if (source is not { IsDisposed: false })
            return null;

        var srv = SrvManager.GetSrvForTexture(source);
        if (srv is not { IsDisposed: false })
            return null;

        if (!TryComputeNdcHomography(destQuad, targetSize, out var homography))
            return null;

        var target = GetOrCreateTarget(_scratchTargetId, targetSize);
        if (target == null || !EnsureShaders())
            return null;

        var deviceContext = ResourceManager.Device.ImmediateContext;
        deviceContext.OutputMerger.SetTargets(target.Rtv);
        deviceContext.Rasterizer.SetViewport(new ViewportF(0, 0, target.Size.Width, target.Size.Height, 0f, 1f));
        deviceContext.ClearRenderTargetView(target.Rtv, new RawColor4(0, 0, 0, 0));

        deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        deviceContext.InputAssembler.InputLayout = null;
        deviceContext.VertexShader.Set(_vertexShaderResource!.Value);
        deviceContext.GeometryShader.Set(null);
        deviceContext.PixelShader.Set(_pixelShaderResource!.Value);
        deviceContext.PixelShader.SetSampler(0, LinearSampler);
        deviceContext.Rasterizer.State = CullNoneRasterizerState;
        deviceContext.OutputMerger.BlendState = DefaultRenderingStates.DefaultBlendState;
        deviceContext.OutputMerger.DepthStencilState = DefaultRenderingStates.DisabledDepthStencilState;

        _shaderParams.Homography = homography;
        _shaderParams.Color = Vector4.One;
        _shaderParams.GridParams = Vector4.Zero; // shared struct — clear any grid mode a prior composite left set
        SetSourceRect(_fullSourceRect);
        ResourceManager.SetupConstBuffer(_shaderParams, ref _paramBuffer);
        deviceContext.VertexShader.SetConstantBuffer(0, _paramBuffer);
        deviceContext.PixelShader.SetConstantBuffer(0, _paramBuffer);
        deviceContext.PixelShader.SetShaderResource(0, srv);
        deviceContext.Draw(6, 0);
        deviceContext.PixelShader.SetShaderResource(0, null);
        return target.Texture;
    }

    /// <summary>The first sink whose target is <paramref name="targetId"/> — a surface (mapped) or an output
    /// (the direct full-frame path).</summary>
    /// <summary>The live texture a content source resolves to, if its op is currently instantiated.</summary>
    public static bool TryGetSourceContent(Guid symbolChildId, out IOutputSink? sink, out Texture2D? content)
    {
        content = null;
        sink = FindSinkByChildId(symbolChildId);
        if (sink == null || _context == null)
            return false;

        content = sink.GetContent(_context);
        return content is { IsDisposed: false };
    }

    /// <summary>
    /// What a surface shows, resolved through the setup: its slice, the source that slice cuts from, and the
    /// live texture behind it.
    /// </summary>
    public static bool TryGetSurfaceSlice(Guid surfaceId, out Slice? slice, out Texture2D? content, out Vector4 uv)
    {
        slice = null;
        content = null;
        uv = _fullSourceRect;

        var setup = ActiveSetup.Current;
        var surface = setup?.FindSurface(surfaceId);
        if (setup == null || surface == null || surface.SliceId == Guid.Empty)
            return false;

        var found = setup.FindSlice(surface.SliceId);
        slice = found;
        var sourceId = found?.SourceId ?? Guid.Empty;
        var source = sourceId == Guid.Empty ? null : setup.FindSource(sourceId);
        if (source == null || !TryGetSourceContent(source.SymbolChildId, out _, out content))
            return false;

        uv = slice!.UvRect;
        return true;
    }

    /// <summary>
    /// Aspect (width/height) of what a surface shows, accounting for its slice — the shape the pixels have
    /// before being fitted onto the surface. Used to un-squeeze the content view.
    /// </summary>
    public static bool TryGetTargetContentAspect(Guid surfaceId, out float aspect)
    {
        aspect = 1f;
        if (!TryGetSurfaceSlice(surfaceId, out _, out var content, out var uv) || content == null)
            return false;

        var width = content.Description.Width * MathF.Max(uv.Z - uv.X, 0.0001f);
        var height = content.Description.Height * MathF.Max(uv.W - uv.Y, 0.0001f);
        if (width <= 0 || height <= 0)
            return false;

        aspect = width / height;
        return true;
    }

    /// <summary>
    /// A slice's live sink and its uv rect: <c>Slice → SourceId → ContentSource → SymbolChildId → op</c>.
    /// Routing is setup data, so it survives the op being re-instantiated.
    /// </summary>
    private static bool TryResolveSliceContent(Setup setup, Guid sliceId, out IOutputSink? sink, out Vector4 sourceRect)
    {
        sink = null;
        sourceRect = _fullSourceRect;
        if (sliceId == Guid.Empty)
            return false;

        var slice = setup.FindSlice(sliceId);
        var source = slice == null ? null : setup.FindSource(slice.SourceId);
        if (source == null)
            return false;

        sink = FindSinkByChildId(source.SymbolChildId);
        sourceRect = slice!.UvRect;
        return sink != null;
    }

    private static bool TryResolveSurfaceContent(Setup setup, T3.Core.Output.Surface surface, out IOutputSink? sink, out Vector4 sourceRect)
    {
        return TryResolveSliceContent(setup, surface.SliceId, out sink, out sourceRect);
    }

    private static IOutputSink? FindSinkByChildId(Guid childId)
    {
        foreach (var sink in OutputSinkRegistry.Sinks)
        {
            if (sink is Instance instance && instance.SymbolChildId == childId)
                return sink;
        }

        return null;
    }

    // Source is a UV rect (xMin, yMin, xMax, yMax); a degenerate rect falls back to the full image.
    private static void SetSourceRect(Vector4 rect)
    {
        if (rect.Z <= rect.X || rect.W <= rect.Y)
            rect = _fullSourceRect;

        // TL, TR, BR, BL — matches the shader cbuffer packing.
        _shaderParams.SourceTlTr = new Vector4(rect.X, rect.Y, rect.Z, rect.Y);
        _shaderParams.SourceBrBl = new Vector4(rect.Z, rect.W, rect.X, rect.W);
    }

    // Unit quad → dest quad (output pixels) → NDC, using the output's own canvas resolution.
    private static bool TryComputeNdcHomography(Vector2[] destQuad, Int2 resolution, out Matrix4x4 matrix)
    {
        if (Homography.TryComputeQuadToQuad(_unitQuad, destQuad, out var unitToPixels))
        {
            var width = Math.Max(resolution.Width, 1);
            var height = Math.Max(resolution.Height, 1);
            var ndcFromPixels = new Homography { M11 = 2.0 / width, M13 = -1, M22 = -2.0 / height, M23 = 1, M33 = 1 };
            matrix = Homography.Multiply(ndcFromPixels, unitToPixels).ToMatrix4x4();
            return true;
        }

        matrix = Matrix4x4.Identity;
        return false;
    }

    private static Target? GetOrCreateTarget(Guid outputId, Int2 resolution)
    {
        var width = Math.Max(1, resolution.Width);
        var height = Math.Max(1, resolution.Height);
        if (_targets.TryGetValue(outputId, out var existing) && existing.Size.Width == width && existing.Size.Height == height)
            return existing;

        existing?.Dispose();

        var description = new Texture2DDescription
                              {
                                  Width = width,
                                  Height = height,
                                  ArraySize = 1,
                                  MipLevels = 1,
                                  BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                                  Usage = ResourceUsage.Default,
                                  CpuAccessFlags = CpuAccessFlags.None,
                                  Format = Format.R16G16B16A16_Float,
                                  OptionFlags = ResourceOptionFlags.None,
                                  SampleDescription = new SampleDescription(1, 0),
                              };

        var texture = Texture2D.CreateTexture2D(description);
        var target = new Target
                         {
                             Texture = texture,
                             Rtv = new RenderTargetView(ResourceManager.Device, texture),
                             Size = new Int2(width, height),
                         };
        _targets[outputId] = target;
        return target;
    }

    private static bool EnsureShaders()
    {
        _vertexShaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.VertexShader>(ShaderPath, null, () => "vsMain");
        _pixelShaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.PixelShader>(ShaderPath, null, () => "psMain");
        return _vertexShaderResource.Value != null && _pixelShaderResource.Value != null;
    }

    private static SamplerState LinearSampler
    {
        get
        {
            if (_linearSampler == null || _linearSampler.IsDisposed)
            {
                _linearSampler = new SamplerState(ResourceManager.Device,
                                                  new SamplerStateDescription
                                                      {
                                                          Filter = Filter.MinMagMipLinear,
                                                          AddressU = TextureAddressMode.Clamp,
                                                          AddressV = TextureAddressMode.Clamp,
                                                          AddressW = TextureAddressMode.Clamp,
                                                          ComparisonFunction = Comparison.Never,
                                                          MaximumLod = float.MaxValue,
                                                      });
            }

            return _linearSampler;
        }
    }

    // Corner-pinned quads can be mirrored or crossed, which flips the winding.
    private static RasterizerState CullNoneRasterizerState
    {
        get
        {
            if (_cullNoneRasterizerState == null || _cullNoneRasterizerState.IsDisposed)
            {
                _cullNoneRasterizerState = new RasterizerState(ResourceManager.Device,
                                                               new RasterizerStateDescription
                                                                   {
                                                                       FillMode = FillMode.Solid,
                                                                       CullMode = CullMode.None,
                                                                       IsDepthClipEnabled = true,
                                                                   });
            }

            return _cullNoneRasterizerState;
        }
    }

    // GridParams.w > 0.5 selects the analytic calibration grid (Srv unused); otherwise Srv is warped as content.
    private readonly record struct DrawItem(ShaderResourceView? Srv, Matrix4x4 Homography, Vector4 SourceRect, Vector4 Color,
                                            Vector4 GridParams, Vector4 GridColor, Vector4 GridOrigin);

    private sealed class Target : IDisposable
    {
        public required Texture2D Texture;
        public required RenderTargetView Rtv;
        public Int2 Size;

        public void Dispose()
        {
            Rtv.Dispose();
            Texture.Dispose();
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct ShaderParams
    {
        public Matrix4x4 Homography;
        public Vector4 SourceTlTr;
        public Vector4 SourceBrBl;
        public Vector4 Color;
        public Vector4 GridParams; // xy = metres spanned, z = line thickness px, w = grid mode
        public Vector4 GridColor;
        public Vector4 GridOrigin; // xy = origin UV, z = minor lines per metre, w = minor opacity
    }

    // Overlay instances. Every member is a float4 so the C# layout and the HLSL structured-buffer packing
    // rules cannot disagree, and the spare lanes leave room to grow (texture slot, dash pattern).
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private readonly record struct OverlayLine(Vector4 Points, Vector4 Color, Vector4 Params);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private readonly record struct OverlayQuad(Vector4 Rect, Vector4 Color, Vector4 Params);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 4)]
    private struct OverlayShaderParams
    {
        public Vector4 TargetSize;
    }

    private const string ShaderPath = "Lib:shaders/dx11/corner-pin-layer.hlsl";
    private const string LineShaderPath = "Lib:shaders/dx11/output-lines.hlsl";
    private const string QuadShaderPath = "Lib:shaders/dx11/output-quads.hlsl";
    private const float _annotationLineWidth = 2.5f;
    private const float _annotationMarkerSize = 11f;
    private const float _aimCrosshairSize = 60f;
    private const float _aimLineWidth = 1.5f;
    private const float _overlayBlinkRate = 8f; // matches the editor-canvas handles so the two stay in phase

    private static readonly Vector2[] _unitQuad = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
    private static readonly Vector4 _fullSourceRect = new(0, 0, 1, 1);
    private static readonly Vector4 _gridColor = new(0.70f, 0.75f, 0.85f, 1); // cool light-gray raster lines
    private const float _gridLineThickness = 1.3f;
    private const float _gridMinorOpacity = 0.35f; // subdivisions sit clearly under the metre lines
    private static readonly Homography _fullscreenNdc = new() { M11 = 2, M13 = -1, M22 = -2, M23 = 1, M33 = 1 };

    // Scratch for a Layout child's derived quad; consumed before the next mapping reuses it.
    private static readonly Vector2[] _childQuadBuffer = new Vector2[4];

    private static readonly Guid _scratchTargetId = new("f1e2d3c4-b5a6-4788-9012-3456789abcde");
    private static readonly Dictionary<Guid, Target> _targets = new();
    private static readonly List<DrawItem> _drawItems = [];

    private static int _presentedDisplayIndex = -1;
    private static EvaluationContext? _context;
    private static int _invalidatedContentFrame = -1;
    private static Resource<T3.Core.DataTypes.VertexShader>? _vertexShaderResource;
    private static Resource<T3.Core.DataTypes.PixelShader>? _pixelShaderResource;
    private static ShaderParams _shaderParams;
    private static Buffer? _paramBuffer;

    private static Guid _aimSurfaceId;
    private static Vector2 _aimInSurface;
    private static int _aimFrame = -10;

    private static Guid _emphasizedSurfaceId;
    private static int _emphasizedIndex = -1;
    private static int _emphasizedFrame = -10;
    private static readonly List<OverlayLine> _overlayLines = [];
    private static readonly List<OverlayQuad> _overlayQuads = [];
    private static Resource<T3.Core.DataTypes.VertexShader>? _lineVertexShader;
    private static Resource<T3.Core.DataTypes.PixelShader>? _linePixelShader;
    private static Resource<T3.Core.DataTypes.VertexShader>? _quadVertexShader;
    private static Resource<T3.Core.DataTypes.PixelShader>? _quadPixelShader;
    private static OverlayShaderParams _overlayParams;
    private static Buffer? _overlayParamBuffer;
    private static Buffer? _lineBuffer;
    private static Buffer? _lineSrvSource;
    private static ShaderResourceView? _lineSrv;
    private static Buffer? _quadBuffer;
    private static Buffer? _quadSrvSource;
    private static ShaderResourceView? _quadSrv;
    private static SamplerState? _linearSampler;
    private static RasterizerState? _cullNoneRasterizerState;
}
