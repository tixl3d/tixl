#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ImGuiNET;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using T3.Core.Output;
using T3.Core.Rendering;
using T3.Core.Resource;
using T3.Editor.UiModel.ProjectHandling;
using Buffer = SharpDX.Direct3D11.Buffer;
using Format = SharpDX.DXGI.Format;
using Texture2D = T3.Core.DataTypes.Texture2D;
using Int2 = T3.Core.DataTypes.Vector.Int2;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Surface = T3.Core.Output.Surface;
using PixelShader = T3.Core.DataTypes.PixelShader;
using VertexShader = T3.Core.DataTypes.VertexShader;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Composites the content bound to a setup output. Walking the active setup's patches and surfaces, it pulls
/// each one's content through <see cref="OutputContentResolver"/>, corner-pin warps the source slice into the
/// output's own render target and returns the composite texture. The send ops never draw — the drawing lives
/// here, in one place.
/// </summary>
internal static class OutputCompositor
{
    // GridParams.w > 0.5 selects the analytic calibration grid (Srv unused); otherwise Srv is warped as content.
    internal readonly record struct DrawItem(ShaderResourceView? Srv, Matrix4x4 Homography, Vector4 SourceRect, Vector4 Color,
                                             Vector4 GridParams, Vector4 GridColor, Vector4 GridOrigin, Vector4 Mask);

    /// <summary>
    /// Renders the output's composite, or null if nothing is bound to it. Rendered at most once per frame:
    /// presentation, the Board card and an open output view all ask for the same pixels, so later calls in
    /// the frame get the target rendered by the first.
    /// </summary>
    public static Texture2D? RenderOutput(Guid outputId)
    {
        var setup = ActiveSetup.Current;
        var output = ActiveSetup.FindOutput(outputId);
        if (setup == null || output == null)
            return null;

        var frame = ImGui.GetFrameCount();
        if (_compositeFrames.TryGetValue(outputId, out var rendered) && rendered.Frame == frame)
            return rendered.HasContent && _targets.TryGetValue(outputId, out var renderedTarget) ? renderedTarget.Texture : null;

        var context = OutputContentResolver.PrepareContext(output.ResolvedResolution);

        // Phase 1: resolve each surface's content and mapping. Pulling content here (before our RT is
        // bound) keeps the content's own rendering from clobbering the target we bind in phase 2.
        _drawItems.Clear();
        CalibrationOverlay.BeginCollect();

        // Patches first: they are the canvas layer (pixels), and the surfaces (the room) composite over them.
        // Painter's order among patches is list order.
        foreach (var patch in output.Patches)
        {
            if (patch.Quad.Length < 4 || !OutputContentResolver.TryResolveSliceContent(setup, patch.SliceId, out var patchSend, out var patchRect))
                continue;

            // A fitted patch asks for its own size, so the content arrives in the patch's aspect; the context
            // goes back to the canvas size for the next consumer.
            var isFittedRequest = OutputContentResolver.TryGetFittedRequest(output, patch, patchRect, out var fittedResolution);
            if (isFittedRequest)
                context.RequestedResolution = fittedResolution;

            var content = OutputContentResolver.PullContent(patchSend!);
            if (isFittedRequest)
                context.RequestedResolution = output.ResolvedResolution;

            if (content is not { IsDisposed: false })
                continue;

            var srv = SrvManager.GetSrvForTexture(content);
            if (srv is not { IsDisposed: false } || !TryComputeNdcHomography(TurnedQuad(patch), out var patchHomography))
                continue;

            _drawItems.Add(new DrawItem(srv, patchHomography, patchRect, patchSend!.GetColor(context), Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero));
        }

        foreach (var surface in setup.Surfaces)
        {
            if (!surface.IsRendered)
                continue;

            // A Layout child usually rides an ancestor's corner pin, so the mappings to walk (and the quad each
            // one yields) come from that ancestor. Regions nest arbitrarily deep, so walk up to whichever one
            // actually holds the pin — which is the region itself when it carries an override mapping. Which of
            // the carrier's mappings applies is decided per output in the loop below.
            var carrier = surface;
            if (surface.Kind == Surface.Kinds.Layout && surface.ParentId != Guid.Empty)
            {
                carrier = setup.FindMappedAncestor(surface.Id);
                if (carrier == null || !carrier.IsRendered)
                    continue;
            }

            OutputContentResolver.TryResolveSurfaceContent(setup, surface, out var supplier, out var resolvedRect);
            var content = supplier == null ? null : OutputContentResolver.PullContent(supplier);
            var srv = content is { IsDisposed: false } ? SrvManager.GetSrvForTexture(content) : null;
            var hasContent = srv is { IsDisposed: false };
            var color = hasContent ? supplier!.GetColor(context) : Vector4.One;
            var sourceRect = hasContent ? resolvedRect : _fullSourceRect;

            // While a surface is being calibrated against its photo, a disc of the photo is projected around each
            // reference point: the wall's own picture, right where the feature is, so it can be walked onto it.
            var projectsPhoto = CalibrationOverlay.ProjectsPhoto(surface.Id);

            // Metres spanned by the surface, and the origin (its anchor) in source UV — the anchor is signed
            // and Y-up while V runs downward from the top.
            var metres = new Vector2(Math.Clamp(surface.SizeInMeters.X, 0.01f, 1000f),
                                     Math.Clamp(surface.SizeInMeters.Y, 0.01f, 1000f));
            var anchor01 = (surface.Anchor + Vector2.One) * 0.5f;
            var gridOrigin = new Vector4(anchor01.X, 1f - anchor01.Y,
                                         Math.Clamp(surface.GridSubdivisions, 1, 100), _gridMinorOpacity);

            foreach (var mapping in carrier.OutputMappings)
            {
                if (mapping.OutputId != outputId)
                    continue;

                var quad = mapping.Quad;
                if (!ReferenceEquals(carrier, surface))
                {
                    // Buffer is consumed by TryComputeNdcHomography before the next iteration reuses it, and
                    // that wants the canvas' 0..1 space — Vector2.One keeps the child's quad in it, where a
                    // pixel size would hand it canvas pixels and throw the region off the canvas entirely.
                    if (!SurfaceGeometry.TryGetRegionQuad(setup, carrier, surface, mapping, Vector2.One, _childQuadBuffer))
                        continue;

                    quad = _childQuadBuffer;
                }

                if (!TryComputeNdcHomography(quad, out var homography))
                    continue;

                if (hasContent)
                    _drawItems.Add(new DrawItem(srv, homography, sourceRect, color, Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero));

                if (projectsPhoto && ReferenceEquals(carrier, surface))
                    CalibrationOverlay.DeferPhotoFragments(surface, mapping, homography);

                // Calibration raster after the content, so it composites *over* it and stays readable while
                // aligning. Emitted with or without content — with none, it's lines on the cleared black.
                if (surface.ShowGrid)
                {
                    _drawItems.Add(new DrawItem(null, homography, _fullSourceRect, Vector4.One,
                                                new Vector4(metres.X, metres.Y, _gridLineThickness, 1), _gridColor, gridOrigin, Vector4.Zero));
                }

                // Annotations have to reach the wall to be usable at all: you align a line by nudging it until
                // its *projection* lies along a real feature, and walk a point onto the feature it marks. They
                // ride the raster's switch or the projected photo — both calibration sessions, neither a show.
                if ((surface.ShowGrid || projectsPhoto) && ReferenceEquals(carrier, surface))
                    CalibrationOverlay.CollectAnnotations(surface, mapping, output.CanvasSize);
            }
        }

        CalibrationOverlay.CollectPhotoFragments(output.ResolvedResolution, _drawItems);

        if (_drawItems.Count == 0)
        {
            _compositeFrames[outputId] = (frame, false);
            return null;
        }

        // 8-bit is what every consumer ends at: a stream sender reads the composite back as 8-bit pixels (NDI
        // accepts nothing else) and a display's swap chain is 8-bit too. A float target would only double the
        // bandwidth of the largest texture in the pipeline — a venue canvas runs to tens of megapixels.
        var target = GetOrCreateTarget(outputId, output.ResolvedResolution, Format.B8G8R8A8_UNorm);
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
            _shaderParams.Mask = item.Mask;
            SetSourceRect(item.SourceRect);
            UploadShaderParams();
            deviceContext.VertexShader.SetConstantBuffer(0, _paramBuffer);
            deviceContext.PixelShader.SetConstantBuffer(0, _paramBuffer);
            deviceContext.PixelShader.SetShaderResource(0, item.Srv);
            deviceContext.Draw(6, 0);
        }

        deviceContext.PixelShader.SetShaderResource(0, null);

        CalibrationOverlay.Draw(deviceContext, output.ResolvedResolution);
        _compositeFrames[outputId] = (frame, true);
        return target.Texture;
    }

    /// <summary>
    /// Warps a texture (sampled full) into a scratch render target so its corners land on
    /// <paramref name="destQuad"/> (in <paramref name="targetSize"/> pixels). Used by the reference-image
    /// straighten transition; returns the warped texture (reused across calls) or null.
    /// </summary>
    /// <param name="targetKey">Which scratch target to render into; callers that need several warps alive in one
    /// frame (each surface card's photo fragment) pass their own key, the default shares one.</param>
    /// <param name="sourceRect">The part of <paramref name="source"/> to warp, as UV min/max; the whole texture by default.</param>
    public static Texture2D? RenderWarpedTexture(Texture2D? source, Vector2[] destQuad, Int2 targetSize, Guid targetKey = default,
                                                 Vector4? sourceRect = null)
    {
        if (source is not { IsDisposed: false })
            return null;

        var srv = SrvManager.GetSrvForTexture(source);
        if (srv is not { IsDisposed: false })
            return null;

        if (!TryComputeNdcHomographyFromPixels(destQuad, targetSize, out var homography))
            return null;

        var target = GetOrCreateTarget(targetKey == Guid.Empty ? _scratchTargetId : targetKey, targetSize, Format.R16G16B16A16_Float);
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
        _shaderParams.Mask = Vector4.Zero; // ...and any fragment disc, or the warp itself comes out masked
        SetSourceRect(sourceRect ?? _fullSourceRect);
        UploadShaderParams();
        deviceContext.VertexShader.SetConstantBuffer(0, _paramBuffer);
        deviceContext.PixelShader.SetConstantBuffer(0, _paramBuffer);
        deviceContext.PixelShader.SetShaderResource(0, srv);
        deviceContext.Draw(6, 0);
        deviceContext.PixelShader.SetShaderResource(0, null);
        return target.Texture;
    }

    /// <summary>Frees every composite target and the per-frame memo; the next frame rebuilds what it needs.</summary>
    public static void ReleaseAll()
    {
        foreach (var target in _targets.Values)
            target.Dispose();

        _targets.Clear();
        _compositeFrames.Clear();
    }

    /// <summary>Frees a deleted output's composite target.</summary>
    public static void ReleaseOutput(Guid outputId)
    {
        if (_targets.Remove(outputId, out var target))
            target.Dispose();

        _compositeFrames.Remove(outputId);
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

    /// <summary>
    /// The patch's quad with its corners shifted by its quarter turns: the source's top-left lands on the
    /// quad's top-right after one turn, so the picture turns clockwise while the quad stays exactly where it
    /// is. Scratch, consumed by the homography before the next patch reuses it.
    /// </summary>
    private static Vector2[] TurnedQuad(OutputDefinition.Patch patch)
    {
        if (patch.QuarterTurns == 0 || patch.Quad.Length < 4)
            return patch.Quad;

        patch.CopyTurnedCorners(_turnedQuad);
        return _turnedQuad;
    }

    /// <summary>Unit quad → a quad in the canvas' own 0..1 space → NDC. No resolution involved: that is the
    /// point of storing mappings normalized.</summary>
    private static bool TryComputeNdcHomography(Vector2[] destQuadNormalized, out Matrix4x4 matrix)
    {
        if (Homography.TryComputeQuadToQuad(_unitQuad, destQuadNormalized, out var unitToCanvas))
        {
            var ndcFromCanvas = new Homography { M11 = 2.0, M13 = -1, M22 = -2.0, M23 = 1, M33 = 1 };
            matrix = Homography.Multiply(ndcFromCanvas, unitToCanvas).ToMatrix4x4();
            return true;
        }

        matrix = Matrix4x4.Identity;
        return false;
    }

    /// <summary>The same for a quad given in pixels of <paramref name="resolution"/> — the warp preview, which
    /// has no canvas of its own.</summary>
    private static bool TryComputeNdcHomographyFromPixels(Vector2[] destQuad, Int2 resolution, out Matrix4x4 matrix)
    {
        var width = MathF.Max(resolution.Width, 1);
        var height = MathF.Max(resolution.Height, 1);
        for (var i = 0; i < 4 && i < destQuad.Length; i++)
            _ndcScratch[i] = new Vector2(destQuad[i].X / width, destQuad[i].Y / height);

        return TryComputeNdcHomography(_ndcScratch, out matrix);
    }

    private static Target? GetOrCreateTarget(Guid outputId, Int2 resolution, Format format)
    {
        var width = Math.Max(1, resolution.Width);
        var height = Math.Max(1, resolution.Height);
        if (_targets.TryGetValue(outputId, out var existing)
            && existing.Size.Width == width && existing.Size.Height == height && existing.Format == format)
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
                                  Format = format,
                                  OptionFlags = ResourceOptionFlags.None,
                                  SampleDescription = new SampleDescription(1, 0),
                              };

        var texture = Texture2D.CreateTexture2D(description);
        var target = new Target
                         {
                             Texture = texture,
                             Rtv = new RenderTargetView(ResourceManager.Device, texture),
                             Size = new Int2(width, height),
                             Format = format,
                         };
        _targets[outputId] = target;
        return target;
    }

    private static bool EnsureShaders()
    {
        _vertexShaderResource ??= ResourceManager.CreateShaderResource<VertexShader>(ShaderPath, null, () => "vsMain");
        _pixelShaderResource ??= ResourceManager.CreateShaderResource<PixelShader>(ShaderPath, null, () => "psMain");
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

    private sealed class Target : IDisposable
    {
        public required Texture2D Texture;
        public required RenderTargetView Rtv;
        public Int2 Size;
        public Format Format;

        public void Dispose()
        {
            Rtv.Dispose();
            Texture.Dispose();
        }
    }

    /// <summary>Creates the constant buffer once, then updates it in place: the compositor uploads once per draw
    /// item every frame, so a staging stream per upload would allocate in the hottest loop of the output path.</summary>
    private static void UploadShaderParams()
    {
        if (_paramBuffer is not { IsDisposed: false })
        {
            _paramBuffer = null;
            ResourceManager.SetupConstBuffer(_shaderParams, ref _paramBuffer);
            return;
        }

        ResourceManager.UpdateConstBuffer(_shaderParams, _paramBuffer);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ShaderParams
    {
        public Matrix4x4 Homography;
        public Vector4 SourceTlTr;
        public Vector4 SourceBrBl;
        public Vector4 Color;
        public Vector4 GridParams; // xy = metres spanned, z = line thickness px, w = grid mode
        public Vector4 GridColor;
        public Vector4 GridOrigin; // xy = origin UV, z = minor lines per metre, w = minor opacity
        public Vector4 Mask; // xy = centre px, z = radius px, w = enabled
    }

    private const string ShaderPath = "Lib:shaders/dx11/corner-pin-layer.hlsl";
    private const float _gridLineThickness = 1.3f;
    private const float _gridMinorOpacity = 0.35f; // subdivisions sit clearly under the metre lines
    private static readonly Vector4 _gridColor = new(0.70f, 0.75f, 0.85f, 1); // cool light-gray raster lines
    private static readonly Vector2[] _unitQuad = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
    private static readonly Vector4 _fullSourceRect = new(0, 0, 1, 1);
    private static readonly Guid _scratchTargetId = new("f1e2d3c4-b5a6-4788-9012-3456789abcde");

    // Scratch quads, each consumed by the homography before the next patch or mapping reuses it.
    private static readonly Vector2[] _turnedQuad = new Vector2[4];
    private static readonly Vector2[] _childQuadBuffer = new Vector2[4];
    private static readonly Vector2[] _ndcScratch = new Vector2[4];

    private static readonly Dictionary<Guid, Target> _targets = new();
    /** Which outputs were composited this frame, and whether anything was drawn. */
    private static readonly Dictionary<Guid, (int Frame, bool HasContent)> _compositeFrames = new();
    private static readonly List<DrawItem> _drawItems = [];
    private static Resource<VertexShader>? _vertexShaderResource;
    private static Resource<PixelShader>? _pixelShaderResource;
    private static ShaderParams _shaderParams;
    private static Buffer? _paramBuffer;
    private static SamplerState? _linearSampler;
    private static RasterizerState? _cullNoneRasterizerState;
}
