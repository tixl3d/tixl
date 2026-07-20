using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using T3.Core.Output;
using T3.Core.Rendering;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.render.output;

[Guid("0b8f2d4e-6a1c-47d3-9f5e-8c2a1b7d4e60")]
internal sealed class OutputLayer : Instance<OutputLayer>
{
    [Output(Guid = "3f9d2c68-1e57-4a0b-9c31-d6b84fa5c7e2")]
    public readonly Slot<Command> Output = new();

    public OutputLayer()
    {
        Output.UpdateAction += Update;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Utilities.Dispose(ref _paramBuffer);
            _stateBackup.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Update(EvaluationContext context)
    {
        var texture = Texture.GetValue(context);
        var color = Color.GetValue(context);
        var outputId = OutputRef.GetValue(context);
        var surfaceId = SurfaceRef.GetValue(context);

        if (texture == null || texture.IsDisposed)
            return;

        _vertexShaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.VertexShader>(ShaderPath, null, () => "vsMain");
        _pixelShaderResource ??= ResourceManager.CreateShaderResource<T3.Core.DataTypes.PixelShader>(ShaderPath, null, () => "psMain");

        var vs = _vertexShaderResource.Value;
        var ps = _pixelShaderResource.Value;
        if (vs == null || ps == null)
            return;

        var srv = SrvManager.GetSrvForTexture(texture);
        if (srv == null || srv.IsDisposed)
            return;

        _shaderParams.Homography = ComputeNdcHomography(surfaceId, outputId, context.RequestedResolution);
        _shaderParams.Color = color;
        ResourceManager.SetupConstBuffer(_shaderParams, ref _paramBuffer);

        var deviceContext = ResourceManager.Device.ImmediateContext;
        _stateBackup.Save(deviceContext);

        deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;
        deviceContext.InputAssembler.InputLayout = null;
        deviceContext.VertexShader.Set(vs);
        deviceContext.VertexShader.SetConstantBuffer(0, _paramBuffer);
        deviceContext.GeometryShader.Set(null);
        deviceContext.PixelShader.Set(ps);
        deviceContext.PixelShader.SetConstantBuffer(0, _paramBuffer);
        deviceContext.PixelShader.SetShaderResource(0, srv);
        deviceContext.PixelShader.SetSampler(0, LinearSampler);
        deviceContext.Rasterizer.State = CullNoneRasterizerState;
        deviceContext.OutputMerger.BlendState = DefaultRenderingStates.DefaultBlendState;
        deviceContext.OutputMerger.DepthStencilState = DefaultRenderingStates.DisabledDepthStencilState;

        deviceContext.Draw(6, 0);

        deviceContext.PixelShader.SetShaderResource(0, null);
        _stateBackup.Restore(deviceContext);
    }

    private static Matrix4x4 ComputeNdcHomography(Guid surfaceId, Guid outputId, Int2 resolution)
    {
        var surface = ActiveSetup.TryFindSurface(surfaceId);
        if (surface != null && ActiveSetup.TryFindOutput(outputId) != null)
        {
            foreach (var mapping in surface.OutputMappings)
            {
                if (mapping.OutputId != outputId)
                    continue;

                if (Homography.TryComputeQuadToQuad(_unitQuad, mapping.Quad, out var unitToPixels))
                {
                    var width = Math.Max(resolution.Width, 1);
                    var height = Math.Max(resolution.Height, 1);
                    var ndcFromPixels = new Homography { M11 = 2.0 / width, M13 = -1, M22 = -2.0 / height, M23 = 1, M33 = 1 };
                    return Homography.Multiply(ndcFromPixels, unitToPixels).ToMatrix4x4();
                }

                break;
            }
        }

        return _fullscreenNdc.ToMatrix4x4();
    }

    /// <summary>
    /// Minimal pipeline-state save/restore for the states this op mutates
    /// (see the fuller variant in _ExecuteBloomPasses).
    /// </summary>
    private sealed class D3D11StateBackup : IDisposable
    {
        public void Save(DeviceContext context)
        {
            if (_isSaved)
                return;

            _topology = context.InputAssembler.PrimitiveTopology;
            _inputLayout = context.InputAssembler.InputLayout;

            var vsStage = context.VertexShader;
            _vertexShader = vsStage.Get();
            _vsConstantBuffers = vsStage.GetConstantBuffers(0, 1);

            _geometryShader = context.GeometryShader.Get();

            var psStage = context.PixelShader;
            _pixelShader = psStage.Get();
            _psConstantBuffers = psStage.GetConstantBuffers(0, 1);
            _psShaderResourceViews = psStage.GetShaderResources(0, 1);
            _psSamplerStates = psStage.GetSamplers(0, 1);

            _rasterizerState = context.Rasterizer.State;
            _blendState = context.OutputMerger.GetBlendState(out _blendFactor, out _sampleMask);
            _depthStencilState = context.OutputMerger.GetDepthStencilState(out _stencilRef);
            _isSaved = true;
        }

        public void Restore(DeviceContext context)
        {
            if (!_isSaved)
                return;

            context.InputAssembler.PrimitiveTopology = _topology;
            context.InputAssembler.InputLayout = _inputLayout;

            context.VertexShader.Set(_vertexShader);
            context.VertexShader.SetConstantBuffers(0, _vsConstantBuffers.Length, _vsConstantBuffers);
            context.GeometryShader.Set(_geometryShader);

            var psStage = context.PixelShader;
            psStage.Set(_pixelShader);
            psStage.SetConstantBuffers(0, _psConstantBuffers.Length, _psConstantBuffers);
            psStage.SetShaderResources(0, _psShaderResourceViews.Length, _psShaderResourceViews);
            psStage.SetSamplers(0, _psSamplerStates.Length, _psSamplerStates);

            context.Rasterizer.State = _rasterizerState;
            context.OutputMerger.SetBlendState(_blendState, _blendFactor, _sampleMask);
            context.OutputMerger.SetDepthStencilState(_depthStencilState, _stencilRef);

            _isSaved = false;
            Dispose();
        }

        /// <summary>
        /// Releases the ref counts the getters in <see cref="Save"/> added; re-binding
        /// in <see cref="Restore"/> takes its own references, so this is always safe.
        /// </summary>
        public void Dispose()
        {
            Utilities.Dispose(ref _inputLayout);
            Utilities.Dispose(ref _vertexShader);
            Utilities.Dispose(ref _geometryShader);
            Utilities.Dispose(ref _pixelShader);
            DisposeAll(_vsConstantBuffers);
            DisposeAll(_psConstantBuffers);
            DisposeAll(_psShaderResourceViews);
            DisposeAll(_psSamplerStates);
            Utilities.Dispose(ref _rasterizerState);
            Utilities.Dispose(ref _blendState);
            Utilities.Dispose(ref _depthStencilState);
            _isSaved = false;
        }

        private static void DisposeAll<T>(T[] items) where T : class, IDisposable
        {
            if (items == null)
                return;

            for (var i = 0; i < items.Length; i++)
            {
                items[i]?.Dispose();
                items[i] = null;
            }
        }

        private PrimitiveTopology _topology;
        private InputLayout _inputLayout;
        private SharpDX.Direct3D11.VertexShader _vertexShader;
        private Buffer[] _vsConstantBuffers;
        private SharpDX.Direct3D11.GeometryShader _geometryShader;
        private SharpDX.Direct3D11.PixelShader _pixelShader;
        private Buffer[] _psConstantBuffers;
        private ShaderResourceView[] _psShaderResourceViews;
        private SamplerState[] _psSamplerStates;
        private RasterizerState _rasterizerState;
        private BlendState _blendState;
        private RawColor4 _blendFactor;
        private int _sampleMask;
        private DepthStencilState _depthStencilState;
        private int _stencilRef;
        private bool _isSaved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ShaderParams
    {
        public Matrix4x4 Homography;
        public Vector4 Color;
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

    // Corner-pinned quads can be mirrored or crossed, which flips the winding
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

    private const string ShaderPath = "Lib:shaders/dx11/corner-pin-layer.hlsl";

    private static readonly Vector2[] _unitQuad = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
    private static readonly Homography _fullscreenNdc = new() { M11 = 2, M13 = -1, M22 = -2, M23 = 1, M33 = 1 };
    private static SamplerState _linearSampler;
    private static RasterizerState _cullNoneRasterizerState;

    private Resource<T3.Core.DataTypes.VertexShader> _vertexShaderResource;
    private Resource<T3.Core.DataTypes.PixelShader> _pixelShaderResource;
    private ShaderParams _shaderParams;
    private Buffer _paramBuffer;
    private readonly D3D11StateBackup _stateBackup = new();

    [Input(Guid = "8a4dd1b3-2e6f-4c25-9d0a-7f3b61c8e942")]
    public readonly InputSlot<T3.Core.DataTypes.Texture2D> Texture = new();

    [Input(Guid = "5c7e19a4-8b3d-4f6e-a201-93d5c4b7f180")]
    public readonly InputSlot<Guid> OutputRef = new();

    [Input(Guid = "e2b64f0d-71a9-4d38-b5c6-08af92d31e75")]
    public readonly InputSlot<Guid> SurfaceRef = new();

    [Input(Guid = "1d83a6f2-49c0-4e17-8b5d-c72e90fa4b36")]
    public readonly InputSlot<Vector4> Color = new();
}
