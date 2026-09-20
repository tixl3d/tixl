// _ExecuteFastBlurPasses.cs
#nullable enable
using T3.Graphics.Compat;
using System.Numerics;
using T3.Core.Rendering;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

using T3.Core.DataTypes.Vector;

namespace Lib.image.fx._;

[Guid("46ce6fad-87fe-4d1f-b236-ae644dd1f76c")]
internal sealed class _ExecuteFastBlurPasses : Instance<_ExecuteFastBlurPasses>
{
 [Output(Guid = "6D01B25B-0B2F-4E92-9B5E-0F6D69F2CF5B")]
    public readonly Slot<Texture2D?> OutputTexture = new();

    public _ExecuteFastBlurPasses()
    {
        OutputTexture.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            UpdateSafe(context);
        }
        catch (Exception ex)
        {
            Log.Warning(" Failed to execute Dual Kawase++ blur " + ex.Message, this);
            OutputTexture.Value = SourceTexture.GetValue(context);
        }
    }
    
    private void UpdateSafe(EvaluationContext context)
    {
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;

        var sourceTexture = SourceTexture.GetValue(context);
        var sourceSrv = SourceTextureSrv.GetValue(context);

        var vs = FullscreenVS.GetValue(context);
        var downPS = DownsampleBlurPS.GetValue(context);
        var upPS = UpsampleBlurPS.GetValue(context);

        var linearSampler = LinearSampler.GetValue(context);

        var stepsIn = Steps.GetValue(context);

        if (sourceTexture == null
            || sourceTexture.IsDisposed
            || sourceSrv == null
            || sourceSrv.IsDisposed
            || vs == null
            || downPS == null
            || upPS == null
            || linearSampler == null)
        {
            Log.Warning("DualKawase++ requires valid inputs.", this);
            OutputTexture.Value = sourceTexture;
            return;
        }

        var initialResolution = new Int2(sourceTexture.Description.Width, sourceTexture.Description.Height);
        var initialFormat = sourceTexture.Description.Format;

        var steps = ResolveSteps(stepsIn, initialResolution);
        if (!InitializeOrUpdateResources(initialResolution, initialFormat, steps))
        {
            OutputTexture.Value = sourceTexture;
            return;
        }

        _stateBackup.Save(deviceContext);

        deviceContext.VertexShader.Set(vs);

        deviceContext.OutputMerger.BlendState = DefaultRenderingStates.DisabledBlendState;
        deviceContext.OutputMerger.DepthStencilState = DefaultRenderingStates.DisabledDepthStencilState;
        deviceContext.Rasterizer.State = DefaultRenderingStates.DefaultRasterizerState;
        deviceContext.InputAssembler.PrimitiveTopology = T3.Graphics.Compat.PrimitiveTopology.TriangleList;

        // Downsample + blur
        var lastSrv = sourceSrv;
        var lastRes = initialResolution;

        deviceContext.PixelShader.SetSampler(0, linearSampler);

        for (var level = 0; level < steps; level++)
        {
            var dst = _levels[level];
            if (dst == null)
                continue;

            var dstRes = dst.Resolution;

            deviceContext.OutputMerger.SetTargets(dst.RTV);
            deviceContext.Rasterizer.SetViewport(new Viewport
            {
                X = 0, Y = 0,
                Width = dstRes.Width, Height = dstRes.Height,
                MinDepth = 0, MaxDepth = 1
            });

            deviceContext.PixelShader.Set(downPS);
            deviceContext.PixelShader.SetShaderResource(0, lastSrv);

            _downParams.InvSrcSize = new Vector2(1f / Math.Max(1, lastRes.Width), 1f / Math.Max(1, lastRes.Height));
            _downParams.OffsetPx = 1.0f; // conservative default; adjust later if you want “stronger per step”
            ResourceManager.SetupConstBuffer(_downParams, ref _downParamsBuffer);
            deviceContext.PixelShader.SetConstantBuffer(0, _downParamsBuffer);

            deviceContext.Draw(3, 0);
            deviceContext.PixelShader.SetShaderResource(0, null);

            lastSrv = dst.SRV;
            lastRes = dstRes;
        }

        // Upsample + blur (Dual Kawase++): write back into higher levels, final into full-res output
        deviceContext.PixelShader.Set(upPS);

        // Up from smallest -> ... -> half res
        for (var level = steps - 2; level >= 0; level--)
        {
            var low = _levels[level + 1];
            var dst = _levels[level];
            if (low == null || dst == null)
                continue;

            deviceContext.OutputMerger.SetTargets(dst.RTV);
            deviceContext.Rasterizer.SetViewport(new Viewport
            {
                X = 0, Y = 0,
                Width = dst.Resolution.Width, Height = dst.Resolution.Height,
                MinDepth = 0, MaxDepth = 1
            });

            deviceContext.PixelShader.SetShaderResource(0, low.SRV);

            FillUpsampleKernel(stageIndex: (steps - 2) - level, stageCount: steps, low.Resolution);
            ResourceManager.SetupConstBuffer(_upParams, ref _upParamsBuffer);
            deviceContext.PixelShader.SetConstantBuffer(0, _upParamsBuffer);

            deviceContext.Draw(3, 0);
            deviceContext.PixelShader.SetShaderResource(0, null);
        }

        // Final up to full resolution
        if (_fullResOutput != null && steps > 0)
        {
            var low = _levels[0];
            if (low != null)
            {
                deviceContext.OutputMerger.SetTargets(_fullResOutput.RTV);
                deviceContext.Rasterizer.SetViewport(new Viewport
                {
                    X = 0, Y = 0,
                    Width = _fullResOutput.Resolution.Width, Height = _fullResOutput.Resolution.Height,
                    MinDepth = 0, MaxDepth = 1
                });

                deviceContext.PixelShader.SetShaderResource(0, low.SRV);

                FillUpsampleKernel(stageIndex: steps - 1, stageCount: steps, low.Resolution);
                ResourceManager.SetupConstBuffer(_upParams, ref _upParamsBuffer);
                deviceContext.PixelShader.SetConstantBuffer(0, _upParamsBuffer);

                deviceContext.Draw(3, 0);
                deviceContext.PixelShader.SetShaderResource(0, null);

                OutputTexture.Value = _fullResOutput.Texture;
            }
            else
            {
                OutputTexture.Value = sourceTexture;
            }
        }
        else
        {
            OutputTexture.Value = sourceTexture;
        }

        _stateBackup.Restore(deviceContext);
    }

    private static int ResolveSteps(int stepsIn, Int2 res)
    {
        if (stepsIn > 0)
            return stepsIn.Clamp(1, 12);

        var minDim = Math.Min(res.Width, res.Height);
        if (minDim <= 1)
            return 1;

        // leave a bit of headroom; avoids spending time on 1x1 tail by default
        var auto = (int)MathF.Floor(MathF.Log2(minDim)) - 2;
        return Math.Max(1, auto).Clamp(1, 12);
    }


    private void FillUpsampleKernel(int stageIndex, int stageCount, Int2 lowRes)
    {
        var t = stageCount <= 1 ? 1f : (float)stageIndex / (stageCount - 1); // 0..1 from deep mip -> final
        var wideC = 2f;
        var wideCard = 2f;
        var wideDiag = 2f;

        // Tight: strong center, lighter diagonals
        var tightC = 8f;
        var tightCard = 2f;
        var tightDiag = 1f;

        var c = Lerp(wideC, tightC, t);
        var card = Lerp(wideCard, tightCard, t);
        var diag = Lerp(wideDiag, tightDiag, t);

        var sum = c + 4f * card + 4f * diag;
        var inv = sum > 1e-8f ? 1f / sum : 1f;

        _upParams.InvLowSize = new Vector2(1f / Math.Max(1, lowRes.Width), 1f / Math.Max(1, lowRes.Height));
        _upParams.OffsetPx = 1.0f;          // you can later vary this per stage if desired
        _upParams.WCenter = c * inv;
        _upParams.WCard = card * inv;
        _upParams.WDiag = diag * inv;
    }

    // Gain/Bias function mapping [0,1] -> [0,1]
    // private static float ApplyGainAndBias(float x, float gain, float bias)
    // {
    //     x = x.Clamp(0f, 1f);
    //
    //     // Bias
    //     var b = bias;
    //     var y = x / (((1f / b - 2f) * (1f - x)) + 1f);
    //
    //     // Gain (symmetric around 0.5)
    //     var g = gain;
    //     if (y < 0.5f)
    //         return 0.5f * (y / (((1f / g - 2f) * (1f - 2f * y)) + 1f));
    //
    //     var y2 = 1f - y;
    //     var r = 0.5f * (y2 / (((1f / g - 2f) * (1f - 2f * y2)) + 1f));
    //     return 1f - r;
    // }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private sealed class RenderTargetSet
    {
        public RenderTargetSet(Device device, Texture2DDescription desc)
        {
            Resolution = new Int2(desc.Width, desc.Height);
            Texture = Texture2D.CreateTexture2D(desc);
            RTV = new RenderTargetView(device, Texture);
            SRV = new ShaderResourceView(device, Texture);
        }

        public Texture2D Texture;
        public RenderTargetView RTV;
        public ShaderResourceView SRV;
        public readonly Int2 Resolution;

        public void Dispose()
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref Texture);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref RTV);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref SRV);
        }
    }

    private readonly List<RenderTargetSet?> _levels = new();
    private RenderTargetSet? _fullResOutput;

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    private struct DownParams
    {
        [FieldOffset(0)] public Vector2 InvSrcSize;
        [FieldOffset(8)] public float OffsetPx;
        [FieldOffset(12)] public float _pad0;
    }

    private DownParams _downParams;
    private Buffer? _downParamsBuffer;

    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct UpParams
    {
        [FieldOffset(0)] public Vector2 InvLowSize;
        [FieldOffset(8)] public float OffsetPx;
        [FieldOffset(12)] public float WCenter;
        [FieldOffset(16)] public float WCard;
        [FieldOffset(20)] public float WDiag;
        [FieldOffset(24)] public Vector2 _pad0;
    }

    private UpParams _upParams;
    private Buffer? _upParamsBuffer;

    private Int2 _lastResolution = Int2.Zero;
    private T3.Graphics.Format _lastFormat = T3.Graphics.Format.Unknown;
    private int _lastSteps = -1;

    private bool InitializeOrUpdateResources(Int2 initialResolution, T3.Graphics.Format initialFormat, int steps)
    {
        var needsRecreate =
            _fullResOutput?.Texture == null ||
            _fullResOutput.Texture.IsDisposed ||
            _levels.Count != steps ||
            _lastResolution != initialResolution ||
            _lastFormat != initialFormat ||
            _lastSteps != steps ||
            (_levels.Count > 0 && (_levels[0]?.Texture == null || _levels[0]!.Texture.IsDisposed));

        var needsBuffers =
            _downParamsBuffer == null || _downParamsBuffer.IsDisposed ||
            _upParamsBuffer == null || _upParamsBuffer.IsDisposed;

        if (!needsRecreate && !needsBuffers)
            return true;

        if (needsRecreate)
        {
            CleanupResources();
        }
        else
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _downParamsBuffer);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _upParamsBuffer);
        }

        try
        {
            var device = ResourceManager.Device;

            if (needsRecreate)
            {
                var fullDesc = new Texture2DDescription
                {
                    Width = initialResolution.Width,
                    Height = initialResolution.Height,
                    Format = initialFormat,
                    BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                    Usage = ResourceUsage.Default,
                    CpuAccessFlags = CpuAccessFlags.None,
                    OptionFlags = ResourceOptionFlags.None,
                    MipLevels = 1,
                    ArraySize = 1,
                    SampleDescription = new T3.Graphics.SampleDescription(1, 0)
                };

                _fullResOutput = new RenderTargetSet(device, fullDesc);

                var cur = initialResolution;
                for (var i = 0; i < steps; i++)
                {
                    cur.Width = Math.Max(1, cur.Width / 2);
                    cur.Height = Math.Max(1, cur.Height / 2);

                    var levelDesc = fullDesc;
                    levelDesc.Width = cur.Width;
                    levelDesc.Height = cur.Height;

                    _levels.Add(new RenderTargetSet(device, levelDesc));
                }

                _lastResolution = initialResolution;
                _lastFormat = initialFormat;
                _lastSteps = steps;
            }

            if (_downParamsBuffer == null)
                ResourceManager.SetupConstBuffer(default(DownParams), ref _downParamsBuffer);

            if (_upParamsBuffer == null)
                ResourceManager.SetupConstBuffer(default(UpParams), ref _upParamsBuffer);

            if (_downParamsBuffer == null || _upParamsBuffer == null)
                throw new Exception("Failed to create constant buffers.");
        }
        catch (Exception e)
        {
            Log.Error("Failed to create DualKawase++ resources: " + e.Message, this);
            CleanupResources();
            return false;
        }

        return true;
    }

    private void CleanupResources()
    {
        foreach (var set in _levels)
            set?.Dispose();
        _levels.Clear();

        _fullResOutput?.Dispose();
        _fullResOutput = null;

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _downParamsBuffer);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _upParamsBuffer);

        _lastResolution = Int2.Zero;
        _lastFormat = T3.Graphics.Format.Unknown;
        _lastSteps = -1;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CleanupResources();
            _stateBackup.Dispose();
        }

        base.Dispose(disposing);
    }

    private readonly D3D11StateBackup _stateBackup = new();

    /// <summary>
    /// Saves and restores the pipeline state a fullscreen effect changes. The facade keeps the state itself,
    /// so this is a push and a pop rather than dozens of reads from the driver — which is also the only shape
    /// Vulkan can support, since there is nothing to read back there.
    /// </summary>
    private sealed class D3D11StateBackup : IDisposable
    {
        public void Save(DeviceContext context)
        {
            if (_isSaved)
                return;

            context.PushState(StateGroups.All);
            _context = context;
            _isSaved = true;
        }

        public void Restore(DeviceContext context)
        {
            if (!_isSaved)
                return;

            context.PopState();
            _isSaved = false;
        }

        public void Dispose()
        {
            if (_isSaved && _context != null)
                Restore(_context);
        }

        private DeviceContext? _context;
        private bool _isSaved;
    }

    [Input(Guid = "692BC2F0-68F2-45CA-A0FB-CD1C5D08E982")]
    public readonly InputSlot<T3.Core.DataTypes.Texture2D> SourceTexture = new();

    [Input(Guid = "98E88D02-3B78-403C-B9C9-B5ECF8565ACD")]
    public readonly InputSlot<ShaderResourceView> SourceTextureSrv = new();

    [Input(Guid = "7F79B69A-5DD8-4C49-9B60-2A2D4C2B5A0F")]
    public readonly InputSlot<int> Steps = new(); // 0 => auto
    

    [Input(Guid = "E6A25147-0739-4416-E8C6-2745C32AD416")]
    public readonly InputSlot<T3.Core.DataTypes.VertexShader> FullscreenVS = new();

    [Input(Guid = "D0B64C21-46C5-45A3-9F68-2E7B2B2CF3A1")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> DownsampleBlurPS = new();

    [Input(Guid = "B82C9D56-19D6-4D2B-9E6F-1A1B1CBE9A25")]
    public readonly InputSlot<T3.Core.DataTypes.PixelShader> UpsampleBlurPS = new();

    [Input(Guid = "5D19C8BE-7EA0-4B8D-5F3D-9EBB3A914B8D")]
    public readonly InputSlot<SamplerState> LinearSampler = new();
}
