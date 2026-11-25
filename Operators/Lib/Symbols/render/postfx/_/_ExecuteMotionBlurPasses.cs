#nullable enable
using System.Numerics;
using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Graphics.Compat;

namespace Lib.render.postfx._;

/// <summary>
/// Runs the four passes of [MotionBlur]: per-pixel blur radius, tile maximum, neighborhood maximum and the gather.
/// The tile passes run at a fraction of the resolution, so they would need a resolution cascade of their own as graph ops.
/// </summary>
[Guid("5e0c7a3b-9d14-4f86-b2a5-7c3e81f04d96")]
internal sealed class _ExecuteMotionBlurPasses : Instance<_ExecuteMotionBlurPasses>
{
    [Output(Guid = "d1f6a8e2-4b37-4c90-8e5d-2a9b07c3f614")]
    public readonly Slot<Texture2D?> Output = new();

    public _ExecuteMotionBlurPasses()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var image = Image.GetValue(context);
        var depthMap = DepthMap.GetValue(context);
        var velocityMap = VelocityMap.GetValue(context);
        var transforms = Transforms.GetValue(context);
        var previousTransforms = PreviousTransforms.GetValue(context);
        var vertexShader = FullscreenVertexShader.GetValue(context);
        var preparePixelShader = PreparePixelShader.GetValue(context);
        var tileMaxPixelShader = TileMaxPixelShader.GetValue(context);
        var neighborMaxPixelShader = NeighborMaxPixelShader.GetValue(context);
        var gatherPixelShader = GatherPixelShader.GetValue(context);
        var sampler = Sampler.GetValue(context);

        var shutter = Shutter.GetValue(context).Clamp(0f, 4f);
        var maxBlur = MaxBlur.GetValue(context).Clamp(1f, 256f);
        var sampleCount = SampleCount.GetValue(context).Clamp(2, 64);

        Output.DirtyFlag.Clear();

        if (image == null || image.IsDisposed || shutter <= 0f
            || vertexShader == null || preparePixelShader == null || tileMaxPixelShader == null
            || neighborMaxPixelShader == null || gatherPixelShader == null || sampler == null)
        {
            Output.Value = image;
            return;
        }

        try
        {
            var resolution = new Int2(image.Description.Width, image.Description.Height);
            var tileSize = Math.Max(MinTileSize, (int)MathF.Ceiling(maxBlur));
            var tileCount = new Int2((resolution.Width + tileSize - 1) / tileSize, (resolution.Height + tileSize - 1) / tileSize);

            if (!UpdateTargets(resolution, tileCount, image.Description.Format))
            {
                Output.Value = image;
                return;
            }

            _params.ShutterScale = shutter;
            _params.MaxBlur = maxBlur;
            _params.SampleCount = sampleCount;
            _params.TileSize = tileSize;
            _params.Resolution = new Vector2(resolution.Width, resolution.Height);
            _params.TileCount = new Vector2(tileCount.Width, tileCount.Height);
            ResourceManager.SetupConstBuffer(_params, ref _paramsBuffer);

            var deviceContext = ResourceManager.Device.ImmediateContext;
            deviceContext.PushState(StateGroups.All);
            try
            {
                deviceContext.VertexShader.Set(vertexShader);
                deviceContext.OutputMerger.BlendState = DefaultRenderingStates.DisabledBlendState;
                deviceContext.OutputMerger.DepthStencilState = DefaultRenderingStates.DisabledDepthStencilState;
                deviceContext.Rasterizer.State = DefaultRenderingStates.DefaultRasterizerState;
                deviceContext.InputAssembler.PrimitiveTopology = PrimitiveTopology.TriangleList;

                var pixelStage = deviceContext.PixelShader;
                pixelStage.SetSampler(0, sampler);
                pixelStage.SetConstantBuffer(0, _paramsBuffer);
                pixelStage.SetConstantBuffer(1, transforms);
                pixelStage.SetConstantBuffer(2, previousTransforms);

                var imageSrv = _imageView.Get(image);
                var depthSrv = _depthView.Get(depthMap);
                var velocitySrv = _velocityView.Get(velocityMap);

                RunPass(deviceContext, _prepared!, preparePixelShader, null, null, depthSrv, velocitySrv);
                RunPass(deviceContext, _tileMax!, tileMaxPixelShader, _prepared!.Srv, null, null, null);
                RunPass(deviceContext, _neighborMax!, neighborMaxPixelShader, null, _tileMax!.Srv, null, null);
                RunPass(deviceContext, _result!, gatherPixelShader, _prepared.Srv, _neighborMax!.Srv, null, null, imageSrv);
            }
            finally
            {
                deviceContext.PopState();
            }

            Output.Value = _result!.Texture;
        }
        catch (Exception e)
        {
            Log.Warning("Motion blur failed: " + e.Message, this);
            Output.Value = image;
        }
    }

    private static void RunPass(DeviceContext deviceContext, Target target, PixelShader pixelShader,
                                ShaderResourceView? prepared, ShaderResourceView? tiles,
                                ShaderResourceView? depth, ShaderResourceView? velocity, ShaderResourceView? image = null)
    {
        var pixelStage = deviceContext.PixelShader;

        // Unbind the previous pass's inputs before one of them becomes this pass's target.
        for (var slot = 0; slot <= 4; slot++)
            pixelStage.SetShaderResource(slot, null);

        deviceContext.OutputMerger.SetTargets(target.Rtv);
        deviceContext.Rasterizer.SetViewport(new Viewport
                                                 {
                                                     X = 0, Y = 0,
                                                     Width = target.Resolution.Width, Height = target.Resolution.Height,
                                                     MinDepth = 0, MaxDepth = 1
                                                 });

        pixelStage.Set(pixelShader);
        pixelStage.SetShaderResource(0, image);
        pixelStage.SetShaderResource(1, depth);
        pixelStage.SetShaderResource(2, velocity);
        pixelStage.SetShaderResource(3, prepared);
        pixelStage.SetShaderResource(4, tiles);
        deviceContext.Draw(3, 0);
    }

    private bool UpdateTargets(Int2 resolution, Int2 tileCount, Format imageFormat)
    {
        if (_result != null && !_result.Texture.IsDisposed
                            && _result.Resolution == resolution && _tileMax!.Resolution == tileCount && _resultFormat == imageFormat)
            return true;

        DisposeTargets();
        try
        {
            _prepared = new Target(resolution, Format.R16G16B16A16_Float);
            _tileMax = new Target(tileCount, Format.R16G16B16A16_Float);
            _neighborMax = new Target(tileCount, Format.R16G16B16A16_Float);
            _result = new Target(resolution, imageFormat);
            _resultFormat = imageFormat;
            return true;
        }
        catch (Exception e)
        {
            Log.Error("Failed to create motion blur targets: " + e.Message, this);
            DisposeTargets();
            return false;
        }
    }

    private void DisposeTargets()
    {
        _prepared?.Dispose();
        _tileMax?.Dispose();
        _neighborMax?.Dispose();
        _result?.Dispose();
        _prepared = _tileMax = _neighborMax = _result = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeTargets();
            _imageView.Dispose();
            _depthView.Dispose();
            _velocityView.Dispose();
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _paramsBuffer);
        }

        base.Dispose(disposing);
    }

    private sealed class Target
    {
        public Target(Int2 resolution, Format format)
        {
            Resolution = resolution;
            Texture = Texture2D.CreateTexture2D(new Texture2DDescription
                                                    {
                                                        Width = resolution.Width,
                                                        Height = resolution.Height,
                                                        Format = format,
                                                        BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                                                        Usage = ResourceUsage.Default,
                                                        CpuAccessFlags = CpuAccessFlags.None,
                                                        OptionFlags = ResourceOptionFlags.None,
                                                        MipLevels = 1,
                                                        ArraySize = 1,
                                                        SampleDescription = new SampleDescription(1, 0)
                                                    });
            Rtv = new RenderTargetView(ResourceManager.Device, Texture);
            Srv = new ShaderResourceView(ResourceManager.Device, Texture);
        }

        public readonly Int2 Resolution;
        public readonly Texture2D Texture;
        public readonly RenderTargetView Rtv;
        public readonly ShaderResourceView Srv;

        public void Dispose()
        {
            Rtv.Dispose();
            Srv.Dispose();
            Texture.Dispose();
        }
    }

    /// <summary>Keeps one view per input texture and recreates it only when the texture changes.</summary>
    private sealed class CachedView
    {
        public ShaderResourceView? Get(Texture2D? texture)
        {
            if (texture == null || texture.IsDisposed)
                return null;

            if (texture == _texture && _view != null)
                return _view;

            Dispose();
            _texture = texture;

            // Depth buffers need an explicit color format to be read.
            _view = (texture.Description.BindFlags & BindFlags.DepthStencil) != 0
                        ? new ShaderResourceView(ResourceManager.Device, texture,
                                                 new ShaderResourceViewDescription
                                                     {
                                                         Format = Format.R32_Float,
                                                         Dimension = ShaderResourceViewDimension.Texture2D,
                                                         Texture2D = new ShaderResourceViewDescription.Texture2DResource
                                                                         {
                                                                             MipLevels = 1,
                                                                             MostDetailedMip = 0
                                                                         }
                                                     })
                        : new ShaderResourceView(ResourceManager.Device, texture);
            return _view;
        }

        public void Dispose()
        {
            _view?.Dispose();
            _view = null;
            _texture = null;
        }

        private Texture2D? _texture;
        private ShaderResourceView? _view;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct Params
    {
        [FieldOffset(0)] public float ShutterScale;
        [FieldOffset(4)] public float MaxBlur;
        [FieldOffset(8)] public float SampleCount;
        [FieldOffset(12)] public float TileSize;
        [FieldOffset(16)] public Vector2 Resolution;
        [FieldOffset(24)] public Vector2 TileCount;
    }

    /// <summary>Small tiles cost more neighborhood passes than they save in the gather.</summary>
    private const int MinTileSize = 16;

    private Params _params;
    private Buffer? _paramsBuffer;
    private Target? _prepared;
    private Target? _tileMax;
    private Target? _neighborMax;
    private Target? _result;
    private Format _resultFormat = Format.Unknown;
    private readonly CachedView _imageView = new();
    private readonly CachedView _depthView = new();
    private readonly CachedView _velocityView = new();

    [Input(Guid = "0a6e4f19-7c25-4b8d-9e13-5f2d8c7b6a40")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "8b3d2c71-e5a4-4f09-a6b8-1c9e07d4f352")]
    public readonly InputSlot<Texture2D> DepthMap = new();

    [Input(Guid = "c47f9e05-2d18-4a63-b9c2-7e5a1f80d3b6")]
    public readonly InputSlot<Texture2D> VelocityMap = new();

    [Input(Guid = "3f81b6d4-9a2e-4c75-8d07-e6b2c459a1f8")]
    public readonly InputSlot<Buffer> Transforms = new();

    [Input(Guid = "e92a5c38-1b6f-4d40-a7e9-0d3c85f7b214")]
    public readonly InputSlot<Buffer> PreviousTransforms = new();

    [Input(Guid = "61d4e7a9-3c08-4b2f-95a6-f8c1b0e2d573")]
    public readonly InputSlot<float> Shutter = new();

    [Input(Guid = "a5c2f8e1-6d93-4e7b-b041-2f7d9c3a8e65")]
    public readonly InputSlot<float> MaxBlur = new();

    [Input(Guid = "17e9b3c6-f42d-4a85-8c1e-9b6a0d5f2c47")]
    public readonly InputSlot<int> SampleCount = new();

    [Input(Guid = "b8f04d2e-5a71-4c3b-9e86-3d1c7f0a5b92")]
    public readonly InputSlot<T3.Core.DataTypes.VertexShader> FullscreenVertexShader = new();

    [Input(Guid = "4c9a1e7f-0b38-4d62-a5f4-8e2b6c1d9a03")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> PreparePixelShader = new();

    [Input(Guid = "d06b5f83-7e2c-4a19-b3d8-5c4f9a2e7b61")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> TileMaxPixelShader = new();

    [Input(Guid = "29e7c4a0-8f15-4b3d-a6c2-0e9d5b7f1c84")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> NeighborMaxPixelShader = new();

    [Input(Guid = "f3a8d6b1-4c97-4e05-8b2a-7d1e3f6c9a50")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> GatherPixelShader = new();

    [Input(Guid = "7d2b9f4e-a163-4c8a-b5e0-c4f81a3d6e29")]
    public readonly InputSlot<SamplerState> Sampler = new();
}
