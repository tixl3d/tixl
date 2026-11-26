using T3.Graphics.Compat;
using T3.Core.Rendering;
using T3.Core.Operator.Interfaces;
using T3.Core.Utils;
using T3.Graphics;
using Device = T3.Graphics.Compat.Device;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.image.generate.basic;

[Guid("f9fe78c5-43a6-48ae-8e8c-6cdbbc330dd1")]
internal sealed class RenderTarget : Instance<RenderTarget>, IRenderStatsProvider, IReleasableGpuResources
{
    /** An extra target beside the color buffer (normals, velocity, material), with its MSAA resolve. */
    private sealed class ColorAttachment(string name, Format format)
    {
        public RenderTargetView Rtv => _multiSampledRtv;

        /** The resolved texture with MSAA, the rendered one without. */
        public Texture2D Texture => _resolved ?? _multiSampled;

        public long SizeInBytes => RenderTarget.SizeInBytes(_multiSampled) + RenderTarget.SizeInBytes(_resolved);

        /** Returns true if the textures were recreated. */
        public bool Update(Device device, Int2 size, int sampleCount, int mipLevels, bool required, Instance owner)
        {
            if (!required)
            {
                Dispose();
                return false;
            }

            var isUpToDate = _multiSampled != null
                             && _multiSampled.Description.Width == size.Width
                             && _multiSampled.Description.Height == size.Height
                             && _multiSampled.Description.SampleDescription.Count == sampleCount;
            if (isUpToDate)
                return false;

            Dispose();
            var downSamplingRequired = sampleCount > 1;
            try
            {
                _multiSampled = Texture2D.CreateTexture2D(CreateDescription(size, downSamplingRequired ? 1 : mipLevels, sampleCount));
                _multiSampledRtv = new RenderTargetView(device, _multiSampled);

                if (downSamplingRequired)
                    _resolved = Texture2D.CreateTexture2D(CreateDescription(size, mipLevels, 1));
            }
            catch (Exception e)
            {
                Dispose();
                Log.Error($"Error creating {name} buffer: {e.Message}", owner);
            }

            return true;
        }

        public void Clear(DeviceContext deviceContext, Vector4 clearValue)
        {
            if (_multiSampledRtv != null)
                deviceContext.ClearRenderTargetView(_multiSampledRtv, clearValue);
        }

        public void Resolve(DeviceContext deviceContext)
        {
            if (_multiSampled != null && _resolved != null)
                deviceContext.ResolveSubresource(_multiSampled, 0, _resolved, 0, _resolved.Description.Format);
        }

        public void Dispose()
        {
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledRtv);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampled);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolved);
        }

        private Texture2DDescription CreateDescription(Int2 size, int mipLevels, int sampleCount)
        {
            return new Texture2DDescription
                       {
                           ArraySize = 1,
                           BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                           CpuAccessFlags = CpuAccessFlags.None,
                           Format = format,
                           Width = size.Width,
                           Height = size.Height,
                           MipLevels = mipLevels,
                           OptionFlags = ResourceOptionFlags.None,
                           SampleDescription = new SampleDescription(sampleCount, 0),
                           Usage = ResourceUsage.Default
                       };
        }

        private Texture2D _multiSampled;
        private RenderTargetView _multiSampledRtv;
        private Texture2D _resolved;
    }

    [Output(Guid = "7A4C4FEB-BE2F-463E-96C6-CD9A6BAD77A2")]
    public readonly Slot<Texture2D> ColorBuffer = new();

    [Output(Guid = "8bb0b18f-4fad-4348-a4fa-95b40c4167a4")]
    public readonly Slot<Texture2D> DepthBuffer = new();

    [Output(Guid = "1CAEFB48-D7FB-40C1-B5B5-C2CF5837DFA9")]
    public readonly Slot<Texture2D> NormalBuffer = new();

    /** Screen-space motion from draws that write it; layout in shared/velocity-functions.hlsl. */
    [Output(Guid = "1c9e78c4-af13-4688-ab51-50360d738f07")]
    public readonly Slot<Texture2D> VelocityBuffer = new();

    /** Unlit surface color of PBR draws (rgb) and their occlusion (a). */
    [Output(Guid = "6df4a69f-917a-4fcb-9fea-d40226b05192")]
    public readonly Slot<Texture2D> BaseColorBuffer = new();

    /** Roughness, metalness and specular of PBR draws (rgb), and how much of the pixel they cover (a, 0 = not relightable). */
    [Output(Guid = "e26a28c5-61bd-46d4-9b1b-41bdef8a6435")]
    public readonly Slot<Texture2D> MaterialBuffer = new();

    public RenderTarget()
    {
        ColorBuffer.UpdateAction += Update;
        DepthBuffer.UpdateAction += Update;
        NormalBuffer.UpdateAction += Update;
        VelocityBuffer.UpdateAction += Update;
        BaseColorBuffer.UpdateAction += Update;
        MaterialBuffer.UpdateAction += Update;
        SetupResolveShaderResources();
        ReleasableGpuResources.Register(this);

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
        var withVelocityBuffer = WithVelocityBuffer.GetValue(context);
        var withMaterialBuffer = WithMaterialBuffer.GetValue(context);
        var clear = Clear.GetValue(context);
        var clearColor = ClearColor.GetValue(context);
        var reference = TextureReference.GetValue(context);
        var enableUpdate = EnableUpdate.GetValue(context);

        // A re-render reproduces the content only if every update starts from a cleared target and nothing
        // outside reads the textures through the reference.
        _isRebuildable = clear && enableUpdate && !TextureReference.HasInputConnections;
            
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
            
        var formatChanged = UpdateTextures(device, size, textureFormat, withDepthBuffer ? Format.R32_Typeless : Format.Unknown, generateMips, withNormalBuffer, withVelocityBuffer,
                                           withMaterialBuffer);

        if (formatChanged || enableUpdate)
        {
            var deviceContext = device.ImmediateContext;

            // Save settings in context
            var prevRequestedResolution = context.RequestedResolution;

            // Saved in one go, and popped further down; the facade tracks the state itself.
            deviceContext.PushState(StateGroups.Rasterizer | StateGroups.OutputMerger);
            
            var prevObjectToWorld = context.ObjectToWorld;
            var prevWorldToCamera = context.WorldToCamera;
            var prevCameraToClipSpace = context.CameraToClipSpace;
            var keepCameraBypass = context.BypassCameras;
            var keepBackgroundColor = context.BackgroundColor;
            var keepForegroundColor = context.ForegroundColor;
            context.BypassCameras = false; 
            context.BackgroundColor = Vector4.One;
            context.ForegroundColor = Vector4.One;
                
                
            deviceContext.Rasterizer.SetViewport(new T3.Graphics.Viewport(0, 0, size.Width, size.Height, 0.0f, 1.0f));
            // Draws write normals to SV_Target1, velocity to SV_Target2, base color and material to SV_Target3/4. Each
            // later target keeps the earlier ones bound: the Vulkan backend packs bound targets, which would shift slots.
            if (withMaterialBuffer && _normalTarget.Rtv != null && _velocityTarget.Rtv != null && _baseColorTarget.Rtv != null
                && _materialTarget.Rtv != null)
            {
                deviceContext.OutputMerger.SetTargets(_multiSampledDepthBufferDsv, _multiSampledColorBufferRtv, _normalTarget.Rtv,
                                                      _velocityTarget.Rtv, _baseColorTarget.Rtv, _materialTarget.Rtv);
            }
            else if (withVelocityBuffer && _normalTarget.Rtv != null && _velocityTarget.Rtv != null)
            {
                deviceContext.OutputMerger.SetTargets(_multiSampledDepthBufferDsv, _multiSampledColorBufferRtv, _normalTarget.Rtv,
                                                      _velocityTarget.Rtv);
            }
            else if (withNormalBuffer && _normalTarget.Rtv != null)
            {
                deviceContext.OutputMerger.SetTargets(_multiSampledDepthBufferDsv, _multiSampledColorBufferRtv, _normalTarget.Rtv);
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
                    deviceContext.ClearRenderTargetView(_multiSampledColorBufferRtv, new Vector4(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W));

                    _normalTarget.Clear(deviceContext, new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                    _velocityTarget.Clear(deviceContext, Vector4.Zero);
                    _baseColorTarget.Clear(deviceContext, Vector4.Zero);
                    _materialTarget.Clear(deviceContext, Vector4.Zero);


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
            if (EvaluationContext.NotifyResolutionWrite(ref _lastWrittenResolution, ref _lastResolutionWriteFrame, size))
            {
                DirtyFlag.GlobalInvalidationTick++;
                Command.InvalidateGraph();
            }

            if (TextureReference.HasInputConnections && TextureReference != null)
            {
                reference.ColorTexture = ColorTexture;
                reference.DepthTexture = DepthTexture;
                reference.NormalTexture = _normalTarget.Texture;
            }

            // Render; draws keep velocity history only when this target can take it.
            var keepRenderPass = context.RenderPass;
            context.RenderPass.VelocityRequested = withVelocityBuffer;
            context.RenderPass.MaterialRequested = withMaterialBuffer;
            context.RenderPass.RenderedCamera = null;
            Command.GetValue(context);
            _bundle.Camera = context.RenderPass.RenderedCamera;
            context.RenderPass = keepRenderPass;

            // Restore context
            context.BypassCameras = keepCameraBypass;
            context.ObjectToWorld = prevObjectToWorld;
            context.WorldToCamera = prevWorldToCamera;
            context.CameraToClipSpace = prevCameraToClipSpace;
            context.RequestedResolution = prevRequestedResolution;
            context.BackgroundColor = keepBackgroundColor;
            context.ForegroundColor = keepForegroundColor;
            deviceContext.PopState();
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

                    _normalTarget.Resolve(device.ImmediateContext);
                    _velocityTarget.Resolve(device.ImmediateContext);
                    _baseColorTarget.Resolve(device.ImmediateContext);
                    _materialTarget.Resolve(device.ImmediateContext);
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

        }
            
        // The color image carries the other buffers, so one connection hands a post effect the whole render.
        // Off by default: image effects render through internal targets whose depth means nothing to their consumers.
        if (AttachBundle.GetValue(context) && ColorTexture != null)
        {
            _bundle.Depth = withDepthBuffer ? DepthTexture : null;
            _bundle.Normal = _normalTarget.Texture;
            _bundle.Velocity = _velocityTarget.Texture;
            _bundle.BaseColor = _baseColorTarget.Texture;
            _bundle.Material = _materialTarget.Texture;
            ColorTexture.Bundle = _bundle;
        }

        ColorBuffer.Value = ColorTexture;
        ColorBuffer.DirtyFlag.Clear();
        DepthBuffer.Value = DepthTexture;
        DepthBuffer.DirtyFlag.Clear();
        NormalBuffer.Value = _normalTarget.Texture;
        NormalBuffer.DirtyFlag.Clear();
        VelocityBuffer.Value = _velocityTarget.Texture;
        VelocityBuffer.DirtyFlag.Clear();
        BaseColorBuffer.Value = _baseColorTarget.Texture;
        BaseColorBuffer.DirtyFlag.Clear();
        MaterialBuffer.Value = _materialTarget.Texture;
        MaterialBuffer.DirtyFlag.Clear();

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
            
        const string sourcePath = "Lib:shaders/dx11/resolve-multisampled-depth-buffer-cs.hlsl"; 
        const string entryPoint = "main";

        _resolveComputeShaderResource = ResourceManager.CreateShaderResource<ComputeShader>(sourcePath, null, () => entryPoint);
            
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
        deviceContext.PushState(StateGroups.ComputeShader);

        csStage.Set(resolveShader);

        const int threadNumX = 16, threadNumY = 16;
        csStage.SetShaderResource(0, _multiSampledDepthBufferSrv);
        csStage.SetUnorderedAccessView(0, _resolvedDepthBufferUav, 0);
        var dispatchCountX = (_multiSampledDepthBuffer.Description.Width / threadNumX) + 1;
        var dispatchCountY = (_multiSampledDepthBuffer.Description.Height / threadNumY) + 1;
        deviceContext.Dispatch(dispatchCountX, dispatchCountY, 1);
            
        deviceContext.PopState();
    }

    private bool UpdateTextures(Device device, Int2 size, Format colorFormat, Format depthFormat, bool generateMips, bool withNormalBuffer,
                                bool withVelocityBuffer, bool withMaterialBuffer)
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
            _statsCountReallocations++;
            // Color / Multi sampling
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledColorBufferSrv);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledColorBufferRtv);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledColorBuffer);

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
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledColorBufferSrv);
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledColorBufferRtv);
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledColorBuffer);
            }

            // Color / Down sampled
            if (_resolvedColorBuffer != null)
            {
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedColorBufferSrv);
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedColorBufferRtv);
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedColorBuffer);
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
                    T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedColorBufferSrv);
                    T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedColorBufferRtv);
                    T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedColorBuffer);
                }
            }

            _wasClearedOnce = false;
        }

        wasChanged |= _normalTarget.Update(device, size, _sampleCount, mipLevels, withNormalBuffer || withVelocityBuffer || withMaterialBuffer,
                                           this);
        wasChanged |= _velocityTarget.Update(device, size, _sampleCount, mipLevels, withVelocityBuffer || withMaterialBuffer, this);
        wasChanged |= _baseColorTarget.Update(device, size, _sampleCount, mipLevels, withMaterialBuffer, this);
        wasChanged |= _materialTarget.Update(device, size, _sampleCount, mipLevels, withMaterialBuffer, this);

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
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledDepthBufferDsv);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledDepthBufferSrv);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledDepthBuffer);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedDepthBufferUav);
            T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedDepthBuffer);
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
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledDepthBufferDsv);
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledDepthBufferSrv);
                T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledDepthBuffer);
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
                    T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedDepthBufferUav);
                    T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedDepthBuffer);
                    Log.Error("Error creating depth/stencil downsampling buffer.", this);
                }
            }
        }

        return wasChanged;
    }


    public long ReleasableBytes => SizeInBytes(_multiSampledColorBuffer) + SizeInBytes(_resolvedColorBuffer)
                                   + SizeInBytes(_multiSampledDepthBuffer) + SizeInBytes(_resolvedDepthBuffer)
                                   + _normalTarget.SizeInBytes + _velocityTarget.SizeInBytes
                                   + _baseColorTarget.SizeInBytes + _materialTarget.SizeInBytes;

    public bool TryReleaseGpuResources()
    {
        if (!_isRebuildable || _multiSampledColorBuffer == null)
            return false;

        DisposeTextures();
        _wasClearedOnce = false;

        // The textures are gone; consumers pulling before the next update must not get them.
        ColorBuffer.Value = null;
        DepthBuffer.Value = null;
        NormalBuffer.Value = null;
        VelocityBuffer.Value = null;
        BaseColorBuffer.Value = null;
        MaterialBuffer.Value = null;
        ColorBuffer.DirtyFlag.ForceInvalidate();
        DepthBuffer.DirtyFlag.ForceInvalidate();
        NormalBuffer.DirtyFlag.ForceInvalidate();
        VelocityBuffer.DirtyFlag.ForceInvalidate();
        BaseColorBuffer.DirtyFlag.ForceInvalidate();
        MaterialBuffer.DirtyFlag.ForceInvalidate();
        return true;
    }

    private static long SizeInBytes(Texture2D texture)
    {
        if (texture == null)
            return 0;

        var description = texture.Description;
        var bytes = (long)description.Width * description.Height
                    * Math.Max(1, FormatSizes.BytesPerPixel(description.Format))
                    * Math.Max(1, description.SampleDescription.Count);

        // A full mip chain adds a third.
        return description.MipLevels > 1 ? bytes * 4 / 3 : bytes;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        ReleasableGpuResources.Unregister(this);
        DisposeTextures();
    }

    private void DisposeTextures()
    {
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledColorBuffer);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledColorBufferSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledColorBufferRtv);

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedColorBuffer);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedColorBufferSrv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedColorBufferRtv);

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledDepthBuffer);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledDepthBufferDsv);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _multiSampledDepthBufferSrv);

        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedDepthBuffer);
        T3.Graphics.Compat.GraphicsUtilities.Dispose(ref _resolvedDepthBufferUav);

        _normalTarget.Dispose();
        _velocityTarget.Dispose();
        _baseColorTarget.Dispose();
        _materialTarget.Dispose();
    }
    

    internal enum Samples
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

    private readonly RenderBundle _bundle = new();
    private readonly ColorAttachment _normalTarget = new("normal", Format.R16G16B16A16_Float);
    private readonly ColorAttachment _velocityTarget = new("velocity", Format.R16G16B16A16_Float);
    private readonly ColorAttachment _baseColorTarget = new("base color", Format.R8G8B8A8_UNorm);
    private readonly ColorAttachment _materialTarget = new("material", Format.R8G8B8A8_UNorm);
 
    private bool _wasClearedOnce;
    private bool _isRebuildable;
    private Int2 _lastWrittenResolution;
    private int _lastResolutionWriteFrame = -1;

    private Texture2D ColorTexture => _sampleCount > 1 ? _resolvedColorBuffer : _multiSampledColorBuffer ;
    private Texture2D DepthTexture => _sampleCount > 1 ? _resolvedDepthBuffer : _multiSampledDepthBuffer;
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

    [Input(Guid = "db4ac4e3-2dd9-4f88-982b-a2e71dcb29f6")]
    public readonly InputSlot<bool> WithVelocityBuffer = new();

    /** Base color and material targets for post effects that relight or reflect; also keeps normal and velocity. */
    [Input(Guid = "e63bb9fb-208b-49d4-8500-aed1f6a74e18")]
    public readonly InputSlot<bool> WithMaterialBuffer = new();

    [Input(Guid = "269ab682-769a-4c9e-acf5-5791ea4735f3")]
    public readonly InputSlot<bool> AttachBundle = new();

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
        yield return ("target re-allocations", _statsCountReallocations);
    }

    private static readonly object _lock = new ();
        
    public void StartNewFrame()
    {
        _statsCount = 0;
        _statsCountPixels = 0;
        _statsCountWithMsaa = 0;
        _statsCountReallocations = 0;
    }
        
    private static int _statsCount;

    /** Re-allocating a target every frame costs far more than drawing into it, and only shows up as a slow
        frame — so consumers asking for different sizes in turn is worth counting rather than guessing at. */
    private static int _statsCountReallocations;
    private static int _statsCountWithMsaa;
    private static int _statsCountPixels;
    private static bool _registeredStats;

    private static Resource<ComputeShader> _resolveComputeShaderResource;
}