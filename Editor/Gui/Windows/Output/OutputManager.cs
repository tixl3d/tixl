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
            var candidate = machineConfig.TryGetBinding(output.Id);
            if (candidate == null)
                continue;

            boundOutput = output;
            binding = candidate;
            break;
        }

        if (boundOutput == null || binding == null)
            return;

        PresentedOutputId = boundOutput.Id;
        ProgramWindows.Viewer.Texture = RenderOutput(boundOutput.Id);

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
            sink.InvalidateContent();

        foreach (var surface in setup.Surfaces)
        {
            var content = FindSink(outputId, surface.Id)?.GetContent(_context);
            if (content is { IsDisposed: false })
                return content;
        }

        return null;
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
            sink.InvalidateContent();

        // Phase 1: resolve each surface's content and mapping. Pulling content here (before our RT is
        // bound) keeps the content's own rendering from clobbering the target we bind in phase 2.
        _drawItems.Clear();
        foreach (var surface in setup.Surfaces)
        {
            var sink = FindSink(outputId, surface.Id);
            if (sink == null)
                continue;

            var content = sink.GetContent(_context);
            if (content == null || content.IsDisposed)
                continue;

            var srv = SrvManager.GetSrvForTexture(content);
            if (srv == null || srv.IsDisposed)
                continue;

            var color = sink.GetColor(_context);
            foreach (var mapping in surface.OutputMappings)
            {
                if (mapping.OutputId != outputId)
                    continue;

                if (TryComputeNdcHomography(mapping.Quad, output.CanvasResolution, out var homography))
                    _drawItems.Add(new DrawItem(srv, homography, mapping.SourceQuad, color));
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
        deviceContext.ClearRenderTargetView(target.Rtv, new RawColor4(0, 0, 0, 0));

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
            SetSourceQuad(item.SourceQuad);
            ResourceManager.SetupConstBuffer(_shaderParams, ref _paramBuffer);
            deviceContext.VertexShader.SetConstantBuffer(0, _paramBuffer);
            deviceContext.PixelShader.SetConstantBuffer(0, _paramBuffer);
            deviceContext.PixelShader.SetShaderResource(0, item.Srv);
            deviceContext.Draw(6, 0);
        }

        deviceContext.PixelShader.SetShaderResource(0, null);
        return target.Texture;
    }

    /// <summary>Sink bound to (output, surface): an exact surface match wins over an output-scoped sink.</summary>
    private static IOutputSink? FindSink(Guid outputId, Guid surfaceId)
    {
        IOutputSink? outputScoped = null;
        foreach (var sink in OutputSinkRegistry.Sinks)
        {
            if (sink.GetOutputId(_context!) != outputId)
                continue;

            var sinkSurface = sink.GetSurfaceId(_context!);
            if (sinkSurface == surfaceId)
                return sink;

            if (sinkSurface == Guid.Empty)
                outputScoped ??= sink;
        }

        return outputScoped;
    }

    private static void SetSourceQuad(Vector2[] source)
    {
        if (source.Length < 4)
            source = _fullSourceQuad;

        // TL, TR, BR, BL — matches the shader cbuffer packing.
        _shaderParams.SourceTlTr = new Vector4(source[0].X, source[0].Y, source[1].X, source[1].Y);
        _shaderParams.SourceBrBl = new Vector4(source[2].X, source[2].Y, source[3].X, source[3].Y);
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

    private readonly record struct DrawItem(ShaderResourceView Srv, Matrix4x4 Homography, Vector2[] SourceQuad, Vector4 Color);

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
    }

    private const string ShaderPath = "Lib:shaders/dx11/corner-pin-layer.hlsl";

    private static readonly Vector2[] _unitQuad = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
    private static readonly Vector2[] _fullSourceQuad = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

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
