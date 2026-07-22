#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
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

        DirtyFlag.GlobalInvalidationTick++;
        foreach (var sink in OutputSinkRegistry.Sinks)
        {
            // Update=false freezes this content at its last frame — skip its invalidation.
            if (sink.GetUpdateEnabled(_context!))
                sink.InvalidateContent();
        }

        foreach (var surface in setup.Surfaces)
        {
            var content = FindSinkForTarget(surface.Id)?.GetContent(_context);
            if (content is { IsDisposed: false })
                return content;
        }

        var direct = FindSinkForTarget(outputId)?.GetContent(_context);
        return direct is { IsDisposed: false } ? direct : null;
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

        // We pull content manually, outside the normal output path, so we must run the same graph
        // invalidation it does — otherwise time-dependent content stays frozen at its cached frame.
        DirtyFlag.GlobalInvalidationTick++;
        foreach (var sink in OutputSinkRegistry.Sinks)
        {
            // Update=false freezes this content at its last frame — skip its invalidation.
            if (sink.GetUpdateEnabled(_context!))
                sink.InvalidateContent();
        }

        // Phase 1: resolve each surface's content and mapping. Pulling content here (before our RT is
        // bound) keeps the content's own rendering from clobbering the target we bind in phase 2.
        _drawItems.Clear();
        foreach (var surface in setup.Surfaces)
        {
            if (!surface.Render)
                continue;

            // Calibration raster: a backdrop for hand-aligning the corner-pin to physical wall features.
            // Emitted regardless of content (drawn first, so any content composites on top of it).
            if (surface.ShowGrid)
            {
                var cells = ComputeGridCells(surface);
                foreach (var mapping in surface.OutputMappings)
                {
                    if (mapping.OutputId != outputId)
                        continue;

                    if (TryComputeNdcHomography(mapping.Quad, output.CanvasResolution, out var gridHomography))
                        _drawItems.Add(new DrawItem(null, gridHomography, _fullSourceRect, Vector4.One,
                                                    new Vector4(cells.X, cells.Y, _gridLineThickness, 1), _gridColor));
                }
            }

            var sink = FindSinkForTarget(surface.Id);
            if (sink == null)
                continue;

            var content = sink.GetContent(_context);
            if (content == null || content.IsDisposed)
                continue;

            var srv = SrvManager.GetSrvForTexture(content);
            if (srv == null || srv.IsDisposed)
                continue;

            var color = sink.GetColor(_context);
            var sourceRect = sink.GetSourceRect(_context);
            foreach (var mapping in surface.OutputMappings)
            {
                if (mapping.OutputId != outputId)
                    continue;

                if (TryComputeNdcHomography(mapping.Quad, output.CanvasResolution, out var homography))
                    _drawItems.Add(new DrawItem(srv, homography, sourceRect, color, Vector4.Zero, Vector4.Zero));
            }
        }

        // No mapped surfaces — full-frame a sink that targets the output directly (Shape 2: the content was
        // rendered through the projector camera, so it already maps 1:1 to the output; no corner-pin warp).
        if (_drawItems.Count == 0)
        {
            var sink = FindSinkForTarget(outputId);
            var content = sink?.GetContent(_context);
            if (content is { IsDisposed: false })
            {
                var srv = SrvManager.GetSrvForTexture(content);
                if (srv is { IsDisposed: false })
                    _drawItems.Add(new DrawItem(srv, _fullscreenNdc.ToMatrix4x4(), sink!.GetSourceRect(_context), sink.GetColor(_context),
                                                Vector4.Zero, Vector4.Zero));
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
            SetSourceRect(item.SourceRect);
            ResourceManager.SetupConstBuffer(_shaderParams, ref _paramBuffer);
            deviceContext.VertexShader.SetConstantBuffer(0, _paramBuffer);
            deviceContext.PixelShader.SetConstantBuffer(0, _paramBuffer);
            deviceContext.PixelShader.SetShaderResource(0, item.Srv);
            deviceContext.Draw(6, 0);
        }

        deviceContext.PixelShader.SetShaderResource(0, null);
        return target.Texture;
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
    /// <summary>
    /// Aspect (width/height) of the content feeding a target, accounting for its source rect — the shape the
    /// source has before it gets fitted onto the surface. Used to un-squeeze it when framing the content.
    /// False when nothing feeds the target (or before the first composite has run).
    /// </summary>
    public static bool TryGetTargetContentAspect(Guid targetId, out float aspect)
    {
        aspect = 1f;
        if (_context == null)
            return false;

        var sink = FindSinkForTarget(targetId);
        var content = sink?.GetContent(_context);
        if (content is not { IsDisposed: false })
            return false;

        var rect = sink!.GetSourceRect(_context);
        var uWidth = rect.Z - rect.X;
        var uHeight = rect.W - rect.Y;
        if (uWidth <= 0 || uHeight <= 0)
        {
            uWidth = 1;
            uHeight = 1;
        }

        var width = content.Description.Width * uWidth;
        var height = content.Description.Height * uHeight;
        if (width <= 0 || height <= 0)
            return false;

        aspect = width / height;
        return true;
    }

    private static IOutputSink? FindSinkForTarget(Guid targetId)
    {
        if (targetId == Guid.Empty)
            return null;

        foreach (var sink in OutputSinkRegistry.Sinks)
        {
            var targets = sink.GetTargetIds(_context!);
            for (var i = 0; i < targets.Count; i++)
            {
                if (targets[i] == targetId)
                    return sink;
            }
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

    // Calibration-grid cell counts across the content canvas = physical size / cell size, clamped so a
    // degenerate cell size or a huge surface can't ask for an unreasonable line count.
    private static Vector2 ComputeGridCells(T3.Core.Output.Surface surface)
    {
        var cellW = MathF.Max(surface.GridCellSize.X, 0.001f);
        var cellH = MathF.Max(surface.GridCellSize.Y, 0.001f);
        return new Vector2(Math.Clamp(surface.SizeInMeters.X / cellW, 1f, 512f),
                           Math.Clamp(surface.SizeInMeters.Y / cellH, 1f, 512f));
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
                                            Vector4 GridParams, Vector4 GridColor);

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
        public Vector4 GridParams; // xy = cell counts, z = line thickness px, w = grid mode
        public Vector4 GridColor;
    }

    private const string ShaderPath = "Lib:shaders/dx11/corner-pin-layer.hlsl";

    private static readonly Vector2[] _unitQuad = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
    private static readonly Vector4 _fullSourceRect = new(0, 0, 1, 1);
    private static readonly Vector4 _gridColor = new(0.70f, 0.75f, 0.85f, 1); // cool light-gray raster lines
    private const float _gridLineThickness = 1.3f;
    private static readonly Homography _fullscreenNdc = new() { M11 = 2, M13 = -1, M22 = -2, M23 = 1, M33 = 1 };

    private static readonly Guid _scratchTargetId = new("f1e2d3c4-b5a6-4788-9012-3456789abcde");
    private static readonly Dictionary<Guid, Target> _targets = new();
    private static readonly List<DrawItem> _drawItems = [];

    private static int _presentedDisplayIndex = -1;
    private static EvaluationContext? _context;
    private static Resource<T3.Core.DataTypes.VertexShader>? _vertexShaderResource;
    private static Resource<T3.Core.DataTypes.PixelShader>? _pixelShaderResource;
    private static ShaderParams _shaderParams;
    private static Buffer? _paramBuffer;
    private static SamplerState? _linearSampler;
    private static RasterizerState? _cullNoneRasterizerState;
}
