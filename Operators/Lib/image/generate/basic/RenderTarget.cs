using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using T3.Core.Utils;
using Device = SharpDX.Direct3D11.Device;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.image.generate.basic;

[Guid("f9fe78c5-43a6-48ae-8e8c-6cdbbc330dd1")]
internal sealed class RenderTarget : Instance<RenderTarget>, IRenderStatsProvider
{
    [Output(Guid = "7A4C4FEB-BE2F-463E-96C6-CD9A6BAD77A2")]
    public readonly Slot<Texture2D> ColorBuffer = new();

    [Output(Guid = "8bb0b18f-4fad-4348-a4fa-95b40c4167a4")]
    public readonly Slot<Texture2D> DepthBuffer = new();

    [Output(Guid = "1CAEFB48-D7FB-40C1-B5B5-C2CF5837DFA9")]
    public readonly Slot<Texture2D> NormalBuffer = new();

    public RenderTarget()
    {
        ColorBuffer.UpdateAction += Update;
        DepthBuffer.UpdateAction += Update;
        NormalBuffer.UpdateAction += Update;
        SetupResolveShaderResources();

        lock (_lock)
        {
            if (!_registeredStats)
            {
                RenderStatsCollector.RegisterProvider(this);
                _registeredStats = true;
            }
        }
    }

    private const int MaximumTexture2DSize = Resource.MaximumTexture2DSize;

    private void Update(EvaluationContext context)
    {
        var device = ResourceManager.Device;
            
        var size = Resolution.GetValue(context);
        var generateMips = GenerateMips.GetValue(context);
        var withDepthBuffer = WithDepthBuffer.GetValue(context);
        var withNormalBuffer = WithNormalBuffer.GetValue(context);
        var clear = Clear.GetValue(context);
        var clearColor = ClearColor.GetValue(context);
        var reference = TextureReference.GetValue(context);
        var enableUpdate = EnableUpdate.GetValue(context);
            
        if (size.Width == 0 || size.Height == 0)
        {
            size = context.RequestedResolution;
        }
            
        if (size.Width <= 0 || size.Height <= 0 || size.Width > MaximumTexture2DSize || size.Height > MaximumTexture2DSize)
        {
            Log.Warning("Ignoring invalid texture size: " + size, this);
            return;
        }

        _sampleCount = Multisampling.GetValue(context).Clamp(1, 32);

        var textureFormat = TextureFormat.GetValue(context);
        if (textureFormat == Format.Unknown)
        {
            Log.Warning("Texture format unknown is not supported. Falling back to default", this);
            textureFormat = TextureFormat.TypedDefaultValue.Value;
            TextureFormat.SetTypedInputValue(textureFormat);
        }
            
        var formatChanged = UpdateTextures(device, size, textureFormat, withDepthBuffer ? Format.R32_Typeless : Format.Unknown, generateMips, withNormalBuffer);

        if (formatChanged || enableUpdate)
        {
            var deviceContext = device.ImmediateContext;

            // Save settings in context
            var prevRequestedResolution = context.RequestedResolution;
            var prevViewports = deviceContext.Rasterizer.GetViewports<RawViewportF>();
            
            // We only use 3 render targets
            const int RtCount = 3;//OutputMergerStage.SimultaneousRenderTargetCount; // 8
            var prevTargets = deviceContext.OutputMerger.GetRenderTargets(RtCount, out var prevDsv);
            
            var prevObjectToWorld = context.ObjectToWorld;
            var prevWorldToCamera = context.WorldToCamera;
            var prevCameraToClipSpace = context.CameraToClipSpace;
            var keepCameraBypass = context.BypassCameras;
            var keepBackgroundColor = context.BackgroundColor;
            var keepForegroundColor = context.ForegroundColor;
            context.BypassCameras = false; 
            context.BackgroundColor = Vector4.One;
            context.ForegroundColor = Vector4.One;
                
                
            deviceContext.Rasterizer.SetViewport(new SharpDX.Viewport(0, 0, size.Width, size.Height, 0.0f, 1.0f));
            // Set render targets - include normal buffer if requested
            if (withNormalBuffer && _multiSampledNormalBufferRtv != null)
            {
                deviceContext.OutputMerger.SetTargets(_multiSampledDepthBufferDsv, _multiSampledColorBufferRtv, _multiSampledNormalBufferRtv);
            }
            else
            {
                deviceContext.OutputMerger.SetTargets(_multiSampledDepthBufferDsv, _multiSampledColorBufferRtv);
            }

            // Clear

            if (clear || !_wasClearedOnce)
            {
                try
                {
                    deviceContext.ClearRenderTargetView(_multiSampledColorBufferRtv, new SharpDX.Color(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));

                    if (_multiSampledNormalBufferRtv != null)
                    {
                        deviceContext.ClearRenderTargetView(_multiSampledNormalBufferRtv, new SharpDX.Color(0.0f, 0.0f, 0.0f, 1.0f));
                    }


                    if (_multiSampledDepthBufferDsv != null)
                    {
                        deviceContext.ClearDepthStencilView(_multiSampledDepthBufferDsv, DepthStencilClearFlags.Depth, 1.0f, 0);
                    }

                    _wasClearedOnce = true;
                } 
                catch
                {
                    Log.Error($"{Parent.Symbol.Name}: Error clearing actual render target.", this);
                }
            }
                
                

            // Set default values for new sub tree
            context.RequestedResolution = size;
            context.SetDefaultCamera();

            if (TextureReference.HasInputConnections && TextureReference != null)
            {
                reference.ColorTexture = ColorTexture;
                reference.DepthTexture = DepthTexture;
                reference.NormalTexture = NormalTexture;
            }

            // Render
            Command.GetValue(context);

            // Restore context
            context.BypassCameras = keepCameraBypass;
            context.ObjectToWorld = prevObjectToWorld;
            context.WorldToCamera = prevWorldToCamera;
            context.CameraToClipSpace = prevCameraToClipSpace;
            context.RequestedResolution = prevRequestedResolution;
            context.BackgroundColor = keepBackgroundColor;
            context.ForegroundColor = keepForegroundColor;
            deviceContext.Rasterizer.SetViewports(prevViewports);
            deviceContext.OutputMerger.SetTargets(prevDsv, prevTargets);
            //deviceContext.OutputMerger.SetTargets(prevDepthStencilView, prevTargets);
                

            if (_sampleCount > 1)
            {
                try
                {
                    device.ImmediateContext.ResolveSubresource(_multiSampledColorBuffer,
                                                               0,
                                                               _resolvedColorBuffer, 0,
                                                               _resolvedColorBuffer.Description.Format);

                    if (withDepthBuffer)
                    {
                        ResolveDepthBuffer();
                    }

                    // Resolve normal buffer if it exists
                    if (withNormalBuffer && _multiSampledNormalBuffer != null && _resolvedNormalBuffer != null)
                    {
                        device.ImmediateContext.ResolveSubresource(_multiSampledNormalBuffer,
                                                                   0,
                                                                   _resolvedNormalBuffer, 0,
                                                                   _resolvedNormalBuffer.Description.Format);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("resolving render target buffer failed:" + e.Message, this);
                }
            }
                
            if (generateMips)
            {
                try
                {
                    deviceContext.GenerateMips(DownSamplingRequired ? _resolvedColorBufferSrv : _multiSampledColorBufferSrv);
                }
                catch (SEHException e)
                {
                    Log.Warning("Failed to generate mipmaps: " + e.Message);
                }
            }

            // Clean up ref counts for RTVs
            for (var i = 0; i < prevTargets.Length; i++)
            {
                prevTargets[i]?.Dispose();
            }

            prevDsv?.Dispose();
        }
            
        ColorBuffer.Value = ColorTexture;
        ColorBuffer.DirtyFlag.Clear();
        DepthBuffer.Value = DepthTexture;
        DepthBuffer.DirtyFlag.Clear();
        NormalBuffer.Value = NormalTexture;
        NormalBuffer.DirtyFlag.Clear();

        _statsCount++;
        _statsCountPixels += size.Height * size.Width;
        if (withDepthBuffer)
        {
            _statsCountPixels += size.Height * size.Width;
        }

        if (_sampleCount > 1)
            _statsCountWithMsaa++;
            
    }
        
    private void SetupResolveShaderResources()
    {
        if (_resolveComputeShaderResource != null && !_resolveComputeShaderResource.IsDisposed)
            return;
            
        const string sourcePath = @"dx11\resolve-multisampled-depth-buffer-cs.hlsl";
        const string entryPoint = "main";

        _resolveComputeShaderResource = ResourceManager.CreateShaderResource<ComputeShader>(sourcePath, this, () => entryPoint);
            
        if (_resolveComputeShaderResource.Value == null)
        {
            Log.Error("Failed to load resolve shader", this);
        }
    }
        
    private void ResolveDepthBuffer()
    {
        if (_resolveComputeShaderResource == null || _resolveComputeShaderResource.IsDisposed)
        {
            SetupResolveShaderResources();
            if (_resolveComputeShaderResource == null)
                return;
        } 

        var resolveShader = _resolveComputeShaderResource.Value;
        if (resolveShader == null)
            return;
            
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;
        var prevShader = csStage.Get();
        var prevUavs = csStage.GetUnorderedAccessViews(0, 1);
        var prevSrvs = csStage.GetShaderResources(0, 1);

        csStage.Set(resolveShader);

        const int threadNumX = 16, threadNumY = 16;
        csStage.SetShaderResource(0, _multiSampledDepthBufferSrv);
        csStage.SetUnorderedAccessView(0, _resolvedDepthBufferUav, 0);
        var dispatchCountX = (_multiSampledDepthBuffer.Description.Width / threadNumX) + 1;
        var dispatchCountY = (_multiSampledDepthBuffer.Description.Height / threadNumY) + 1;
        deviceContext.Dispatch(dispatchCountX, dispatchCountY, 1);
            
        // Restore prev setup
        csStage.SetUnorderedAccessView(0, prevUavs[0]);
        csStage.SetShaderResource(0, prevSrvs[0]);
        csStage.Set(prevShader);
    }

    private bool UpdateTextures(Device device, Int2 size, Format colorFormat, Format depthFormat, bool generateMips, bool withNormalBuffer)
    {
        var w = Math.Max(size.Width, size.Height);
        var mipLevels = generateMips ? (int)MathUtils.Log2(w) + 1 : 1;
        var multiSampleTexture2dMipLevels = DownSamplingRequired ? 1 : mipLevels;
        var wasChanged = false;

        var colorFormatChanged = _multiSampledColorBuffer == null
                                  || _multiSampledColorBuffer.Description.Format != colorFormat
                                  || _multiSampledColorBuffer.Description.MipLevels != multiSampleTexture2dMipLevels
                                  || _multiSampledColorBuffer.Description.Width != size.Width
                                  || _multiSampledColorBuffer.Description.Height != size.Height
                                  || _multiSampledColorBuffer.Description.SampleDescription.Count != _sampleCount;

        if (colorFormatChanged)
        {
            wasChanged = true;
            // Color / Multi sampling
            Utilities.Dispose(ref _multiSampledColorBufferSrv);
            Utilities.Dispose(ref _multiSampledColorBufferRtv);
            Utilities.Dispose(ref _multiSampledColorBuffer);

            try
            {
                    
                var texture2DDescription = new Texture2DDescription
                                               {
                                                   ArraySize = 1,
                                                   BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                                                   CpuAccessFlags = CpuAccessFlags.None,
                                                   Format = colorFormat,
                                                   Width = size.Width,
                                                   Height = size.Height,
                                                   MipLevels = !DownSamplingRequired ? mipLevels : 1,
                                                   OptionFlags = !DownSamplingRequired && generateMips
                                                                     ? ResourceOptionFlags.GenerateMipMaps
                                                                     : ResourceOptionFlags.None,
                                                   SampleDescription = new SampleDescription(_sampleCount, 0),
                                                   Usage = ResourceUsage.Default,
                                               };
                _multiSampledColorBuffer = Texture2D.CreateTexture2D(texture2DDescription);

                _multiSampledColorBufferSrv = new ShaderResourceView(device, _multiSampledColorBuffer);
                _multiSampledColorBufferRtv = new RenderTargetView(device, _multiSampledColorBuffer,
                                                                   new RenderTargetViewDescription
                                                                       {
                                                                           Format = colorFormat,
                                                                           Dimension = DownSamplingRequired
                                                                                           ? RenderTargetViewDimension.Texture2DMultisampled
                                                                                           : RenderTargetViewDimension.Texture2D
                                                                       });

                _wasClearedOnce = false;
            }
            catch (Exception e)
            {
                Log.Error("Error creating color render target." + e.Message, this);
                Utilities.Dispose(ref _multiSampledColorBufferSrv);
                Utilities.Dispose(ref _multiSampledColorBufferRtv);
                Utilities.Dispose(ref _multiSampledColorBuffer);
            }

            // Color / Down sampled
            if (_resolvedColorBuffer != null)
            {
                Utilities.Dispose(ref _resolvedColorBufferSrv);
                Utilities.Dispose(ref _resolvedColorBufferRtv);
                Utilities.Dispose(ref _resolvedColorBuffer);
            }

            if (DownSamplingRequired)
            {
                try
                {
                    _resolvedColorBuffer = Texture2D.CreateTexture2D(
                                                                     new Texture2DDescription
                                                                         {
                                                                             ArraySize = 1,
                                                                             BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                                                                             CpuAccessFlags = CpuAccessFlags.None,
                                                                             Format = colorFormat,
                                                                             Width = size.Width,
                                                                             Height = size.Height,
                                                                             MipLevels = mipLevels,
                                                                             OptionFlags =
                                                                                 generateMips ? ResourceOptionFlags.GenerateMipMaps : ResourceOptionFlags.None,
                                                                             SampleDescription = new SampleDescription(1, 0),
                                                                             Usage = ResourceUsage.Default
                                                                         });
                    _resolvedColorBufferSrv = new ShaderResourceView(device, _resolvedColorBuffer);
                    _resolvedColorBufferRtv = new RenderTargetView(device, _resolvedColorBuffer);
                    _wasClearedOnce = false;
                }
                catch
                {
                    Log.Error("Error creating color render target.", this);
                    Utilities.Dispose(ref _resolvedColorBufferSrv);
                    Utilities.Dispose(ref _resolvedColorBufferRtv);
                    Utilities.Dispose(ref _resolvedColorBuffer);
                }
            }

            _wasClearedOnce = false;
        }

        var normalRequired = withNormalBuffer;
        var normalInitialized = _multiSampledNormalBuffer != null;

        var normalFormatChanged = (_multiSampledNormalBuffer == null)
                                  || _multiSampledNormalBuffer.Description.Width != size.Width
                                  || _multiSampledNormalBuffer.Description.Height != size.Height
                                  || _multiSampledNormalBuffer.Description.SampleDescription.Count != _sampleCount;

        if (normalFormatChanged || (!normalRequired && normalInitialized))
        {
            Utilities.Dispose(ref _multiSampledNormalBufferRtv);
            Utilities.Dispose(ref _resolvedNormalBufferRtv);
            Utilities.Dispose(ref _resolvedNormalBufferSrv);
            Utilities.Dispose(ref _multiSampledNormalBuffer);
            Utilities.Dispose(ref _resolvedNormalBuffer);
        }

        if (normalRequired && (normalFormatChanged || !normalInitialized))
        {
            wasChanged = true;

            // Normal / Multi sampled
            try
            {
                _multiSampledNormalBuffer = Texture2D.CreateTexture2D(
                    new Texture2DDescription
                        {
                            ArraySize = 1,
                            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                            CpuAccessFlags = CpuAccessFlags.None,
                            Format = Format.R16G16B16A16_Float,
                            Width = size.Width,
                            Height = size.Height,
                            MipLevels = DownSamplingRequired ? 1 : mipLevels,
                            OptionFlags = ResourceOptionFlags.None,
                            SampleDescription = new SampleDescription(_sampleCount, 0),
                            Usage = ResourceUsage.Default
                        });
                _multiSampledNormalBufferRtv = new RenderTargetView(device, _multiSampledNormalBuffer);
            }
            catch (Exception e)
            {
                Utilities.Dispose(ref _multiSampledNormalBuffer);
                Utilities.Dispose(ref _multiSampledNormalBufferRtv);
                Log.Error("Error creating multisampled normal buffer: " + e.Message, this);
            }

            // Normal / Resolved (for MSAA)
            if (DownSamplingRequired)
            {
                try
                {
                    _resolvedNormalBuffer = Texture2D.CreateTexture2D(
                        new Texture2DDescription
                            {
                                ArraySize = 1,
                                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                                CpuAccessFlags = CpuAccessFlags.None,
                                Format = Format.R16G16B16A16_Float,
                                Width = size.Width,
                                Height = size.Height,
                                MipLevels = mipLevels,
                                OptionFlags = ResourceOptionFlags.None,
                                SampleDescription = new SampleDescription(1, 0),
                                Usage = ResourceUsage.Default
                            });

                    _resolvedNormalBufferSrv = new ShaderResourceView(device, _resolvedNormalBuffer);
                    _resolvedNormalBufferRtv = new RenderTargetView(device, _resolvedNormalBuffer);
                }
                catch (Exception e)
                {
                    Utilities.Dispose(ref _resolvedNormalBuffer);
                    Utilities.Dispose(ref _resolvedNormalBufferSrv);
                    Utilities.Dispose(ref _resolvedNormalBufferRtv);
                    Log.Error("Error creating resolved normal buffer: " + e.Message, this);
                }
            }
            else
            {
                // For non-MSAA, create SRV for the multisampled buffer
                try
                {
                    _resolvedNormalBufferSrv = new ShaderResourceView(device, _multiSampledNormalBuffer);
                }
                catch (Exception e)
                {
                    Log.Error("Error creating normal buffer SRV: " + e.Message, this);
                }
            }
        }

        // Depth buffer handling
        var depthRequired = depthFormat != Format.Unknown;
        var depthInitialized = _multiSampledDepthBuffer != null;

        var depthFormatChanged = (_multiSampledDepthBuffer == null)
                                  || _multiSampledDepthBuffer.Description.Width != size.Width
                                  || _multiSampledDepthBuffer.Description.Height != size.Height
                                  || _multiSampledDepthBuffer.Description.SampleDescription.Count != _sampleCount
                                  || _multiSampledDepthBuffer.Description.Format != depthFormat;

        if (depthFormatChanged || (!depthRequired && depthInitialized))
        {
            Utilities.Dispose(ref _multiSampledDepthBufferDsv);
            Utilities.Dispose(ref _multiSampledDepthBufferSrv);
            Utilities.Dispose(ref _multiSampledDepthBuffer);
            Utilities.Dispose(ref _resolvedDepthBufferUav);
            Utilities.Dispose(ref _resolvedDepthBuffer);
        }

        if (depthRequired && (depthFormatChanged || !depthInitialized))
        {
            wasChanged = true;
            // Depth / Multi sampled
            try
            {
                _multiSampledDepthBuffer = Texture2D.CreateTexture2D(
                                                                     new Texture2DDescription
                                                                         {
                                                                             ArraySize = 1,
                                                                             BindFlags = BindFlags.DepthStencil | BindFlags.ShaderResource,
                                                                             CpuAccessFlags = CpuAccessFlags.None,
                                                                             Format = Format.R32_Typeless,
                                                                             Width = size.Width,
                                                                             Height = size.Height,
                                                                             MipLevels = DownSamplingRequired ? 1 : mipLevels,
                                                                             OptionFlags = ResourceOptionFlags.None,
                                                                             SampleDescription = new SampleDescription(_sampleCount, 0),
                                                                             Usage = ResourceUsage.Default
                                                                         });

                _multiSampledDepthBufferDsv = new DepthStencilView(device,
                                                                   _multiSampledDepthBuffer,
                                                                   new DepthStencilViewDescription
                                                                       {
                                                                           Format = Format.D32_Float,
                                                                           Dimension = DownSamplingRequired
                                                                                           ? DepthStencilViewDimension.Texture2DMultisampled
                                                                                           : DepthStencilViewDimension.Texture2D
                                                                       });
                if (DownSamplingRequired)
                {
                    var viewDesc = new ShaderResourceViewDescription
                    {
                        Format = Format.R32_Float,
                        Dimension = ShaderResourceViewDimension.Texture2DMultisampled
                    };
                    _multiSampledDepthBufferSrv = new ShaderResourceView(device, _multiSampledDepthBuffer, viewDesc);
                }
            }
            catch
            {
                Utilities.Dispose(ref _multiSampledDepthBufferDsv);
                Utilities.Dispose(ref _multiSampledDepthBufferSrv);
                Utilities.Dispose(ref _multiSampledDepthBuffer);
                Log.Error("Error  creating multisampled depth/stencil buffer.", this);
            }

            if (DownSamplingRequired)
            {
                try
                {
                    _resolvedDepthBuffer = Texture2D.CreateTexture2D(
                                                                     new Texture2DDescription
                                                                         {
                                                                             ArraySize = 1,
                                                                             BindFlags = BindFlags.ShaderResource | BindFlags.UnorderedAccess,
                                                                             CpuAccessFlags = CpuAccessFlags.None,
                                                                             Format = Format.R32_Float,
                                                                             Width = size.Width,
                                                                             Height = size.Height,
                                                                             MipLevels = mipLevels,
                                                                             OptionFlags = ResourceOptionFlags.None,
                                                                             SampleDescription = new SampleDescription(1, 0),
                                                                             Usage = ResourceUsage.Default
                                                                         });

                    _resolvedDepthBufferUav = new UnorderedAccessView(device, _resolvedDepthBuffer);
                }
                catch
                {
                    Utilities.Dispose(ref _resolvedDepthBufferUav);
                    Utilities.Dispose(ref _resolvedDepthBuffer);
                    Log.Error("Error creating depth/stencil downsampling buffer.", this);
                }
            }
        }

        return wasChanged;
    }


    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        //Log.Debug("Disposing RenderTarget", this);
        
        Utilities.Dispose(ref _multiSampledColorBuffer);
        Utilities.Dispose(ref _multiSampledColorBufferSrv);
        Utilities.Dispose(ref _multiSampledColorBufferRtv);

        Utilities.Dispose(ref _resolvedColorBuffer);
        Utilities.Dispose(ref _resolvedColorBufferSrv);
        Utilities.Dispose(ref _resolvedColorBufferRtv);

        Utilities.Dispose(ref _multiSampledDepthBuffer);
        Utilities.Dispose(ref _multiSampledDepthBufferDsv);
        Utilities.Dispose(ref _multiSampledDepthBufferSrv);

        Utilities.Dispose(ref _resolvedDepthBuffer);
        Utilities.Dispose(ref _resolvedDepthBufferUav);

        Utilities.Dispose(ref _resolvedNormalBuffer);
        Utilities.Dispose(ref _resolvedNormalBufferSrv);
        Utilities.Dispose(ref _resolvedNormalBufferRtv);

        Utilities.Dispose(ref _multiSampledNormalBuffer);
        Utilities.Dispose(ref _multiSampledNormalBufferRtv);
    }
    

    private enum Samples
    {
        No_MSAA = 1,
        MSAA_2Samples =2,
        MSAA_4Samples = 4,
        MSAA_8Samples = 8,
    }
        
    private Texture2D _multiSampledColorBuffer;
    private ShaderResourceView _multiSampledColorBufferSrv;
    private RenderTargetView _multiSampledColorBufferRtv;

    private Texture2D _resolvedColorBuffer;
    private ShaderResourceView _resolvedColorBufferSrv;
    private RenderTargetView _resolvedColorBufferRtv;

    private Texture2D _multiSampledDepthBuffer;
    private DepthStencilView _multiSampledDepthBufferDsv;
    private ShaderResourceView _multiSampledDepthBufferSrv;

    private Texture2D _resolvedDepthBuffer;
    private UnorderedAccessView _resolvedDepthBufferUav;

    private Texture2D _resolvedNormalBuffer;
    private ShaderResourceView _resolvedNormalBufferSrv;
    private RenderTargetView _resolvedNormalBufferRtv;

    private Texture2D _multiSampledNormalBuffer;
    private RenderTargetView _multiSampledNormalBufferRtv;
 
    private bool _wasClearedOnce;

    private Texture2D ColorTexture => _sampleCount > 1 ? _resolvedColorBuffer : _multiSampledColorBuffer ;
    private Texture2D DepthTexture => _sampleCount > 1 ? _resolvedDepthBuffer : _multiSampledDepthBuffer;
    private Texture2D NormalTexture => _sampleCount > 1 ? _resolvedNormalBuffer : _multiSampledNormalBuffer;
    private bool DownSamplingRequired => _sampleCount > 1;
    private int _sampleCount;

    [Input(Guid = "4da253b7-4953-439a-b03f-1d515a78bddf")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "03749b41-cc3c-4f38-aea6-d7cea19fc073")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "aacafc4d-f47f-4893-9a6e-98db306a8901")]
    public readonly InputSlot<bool> Clear = new();
        
    [Input(Guid = "8bb4a4e5-0c88-4d99-a5b2-2c9e22bd301f")]
    public readonly InputSlot<Vector4> ClearColor = new();
        
    [Input(Guid = "E882E0F0-03F9-46E6-AC7A-709E6FA66613", MappedType = typeof(Samples))]
    public readonly InputSlot<int> Multisampling = new();
        
    [Input(Guid = "ec46bef4-8dce-4eb4-bfe8-e35a5ac416ec")]
    public readonly InputSlot<Format> TextureFormat = new();

    // [Input(Guid = "2d54adbb-04c7-4f92-babd-9822953aa4cd")]
    // public readonly InputSlot<Format> DepthFormat = new InputSlot<Format>();
        
    [Input(Guid = "6EA4F801-FF52-4266-A41F-B9EF02C68510")]
    public readonly InputSlot<bool> WithDepthBuffer = new();

    [Input(Guid = "3782E42E-6A36-4BA2-AAA2-3EE401A3CDF1")]
    public readonly InputSlot<bool> WithNormalBuffer = new();

    [Input(Guid = "f0cf3325-4967-4419-9beb-036cd6dbfd6a")]
    public readonly InputSlot<bool> GenerateMips = new();

    [Input(Guid = "07AD28AD-FF5F-4CA9-B7BB-F7F8B16A6434")]
    public readonly InputSlot<RenderTargetReference> TextureReference = new();
        
    [Input(Guid = "ABEBB02B-BCAA-42C7-8EB8-DA3C1B2FC840")]
    public readonly InputSlot<bool> EnableUpdate = new();

    public IEnumerable<(string, int)> GetStats()
    {
        yield return ("RenderTargets", _statsCount);
        yield return ("with MSAA", _statsCountWithMsaa);
        yield return ("pixels", _statsCountPixels);
    }

    private static readonly object _lock = new ();
        
    public void StartNewFrame()
    {
        _statsCount = 0;
        _statsCountPixels = 0;
        _statsCountWithMsaa = 0;
    }
        
    private static int _statsCount;
    private static int _statsCountWithMsaa;
    private static int _statsCountPixels;
    private static bool _registeredStats;

    private static Resource<ComputeShader> _resolveComputeShaderResource;
}