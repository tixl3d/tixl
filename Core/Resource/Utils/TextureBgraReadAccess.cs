#nullable enable

using System;
using System.Collections.Generic;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Resource.Assets;
using T3.Core.UserData;
using ComputeShader = T3.Core.DataTypes.ComputeShader;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Core.Resource;

/// <summary>
/// Uses a compute shader to converts an image texture into a BGRA8
/// and provide CPU read access for writing to images and video files. 
/// </summary>
public sealed class TextureBgraReadAccess : IDisposable
{
    private readonly bool _useImmediateReadback;
    private readonly Format _targetFormat;

    public TextureBgraReadAccess(bool useImmediateReadback = false, Format targetFormat = Format.B8G8R8A8_UNorm)
    {
        _useImmediateReadback = useImmediateReadback;
        _targetFormat = targetFormat;
    }


    
    
    /// <summary>
    /// A helper method that uses a compute shader to convert the source texture first to 8bit. 
    /// </summary>
    public Texture2D ConvertToCpuReadableBgra(Texture2D sourceTexture)
    {
        if (sourceTexture == null! || sourceTexture.IsDisposed)
            throw new ArgumentException("Source texture is null or disposed");

        PrepareCpuAccessTexturesAndConversionTextures(sourceTexture.Description);
        PrepareResolveShaderResources();
        ConvertTextureToRgba(sourceTexture);

        var context = ResourceManager.Device.ImmediateContext;
        var cpuAccessTexture = _imagesWithCpuAccess[0];
        context.CopyResource(_conversionTexture, cpuAccessTexture);
        context.Flush();  // Ensure the copy is complete

        return cpuAccessTexture;
    }
    
    public sealed class ReadRequestItem
    {
        internal int RequestIndex;
        public required TextureBgraReadAccess TextureBgraReadAccess;
        
        /// <summary>
        /// An optional filepath that can be passed along
        /// </summary>
        public required string? Filepath;
        public required Texture2D CpuAccessTexture;
        public required OnReadComplete OnSuccess;
        public required int CpuAccessTextureCount;


        internal bool IsReady => RequestIndex == TextureBgraReadAccess._swapCounter - (CpuAccessTextureCount - 2);
        internal bool IsObsolete => RequestIndex < TextureBgraReadAccess._swapCounter - (CpuAccessTextureCount - 2);
    }

    /// <summary>
    /// Saving a screenshot will take several frames because it takes a while until the frames are
    /// downloaded from the gpu. The method need to be called once a frame.
    /// </summary>
    public void Update()
    {
        _swapCounter++;

        // Clear obsolete items
        while (_readRequests.Count > 0 && _readRequests[0].IsObsolete)
        {
            _readRequests.RemoveAt(0);
        }
        
        if (_readRequests.Count == 0 || !_readRequests[0].IsReady)
            return;

        var completedRequest = _readRequests[0];
        //Log.Debug($"Completed frame i{completedRequest.RequestIndex} Run:{completedRequest.RequestRunTime:0.000}s Play:{completedRequest.RequestPlayback:0.000}s   completed {Playback.RunTimeInSecs:0.000}");
        
        completedRequest.OnSuccess(completedRequest);
        
        _readRequests.RemoveAt(0);
    }

    public delegate void OnReadComplete(ReadRequestItem requestItem);

    
    /// <summary>
    /// Convert into BRGA and initiate the readback process.
    /// It will take several frames, until the texture is accessible and the callback is called.
    /// </summary>
    public bool InitiateConvertAndReadBack(Texture2D originalTexture, OnReadComplete onSuccess, string? filepath = null)
    {
        if (originalTexture == null! || originalTexture.IsDisposed)
            return false;

        PrepareCpuAccessTexturesAndConversionTextures(originalTexture.Description);
        PrepareResolveShaderResources();
        ConvertTextureToRgba(originalTexture);

        var context = ResourceManager.Device.ImmediateContext;
        var cpuAccessTexture = _imagesWithCpuAccess[_swapCounter % CpuAccessTextureCount];
        context.CopyResource(_conversionTexture, cpuAccessTexture);

        if (_useImmediateReadback)
        {
            context.Flush();  
            
            //var dataBox = context.MapSubresource(cpuAccessTexture, 0, MapMode.Read, MapFlags.None);
            try
            {
                var request = new ReadRequestItem
                                  {
                                      RequestIndex = _swapCounter,
                                      TextureBgraReadAccess = this,
                                      Filepath = filepath,
                                      CpuAccessTexture = cpuAccessTexture,
                                      OnSuccess = onSuccess,
                                      CpuAccessTextureCount = CpuAccessTextureCount,
                                  };
                onSuccess(request);
            }
            finally
            {
                context.UnmapSubresource(cpuAccessTexture, 0);
            }
        }
        else
        {
            context.UnmapSubresource(cpuAccessTexture, 0);
            _readRequests.Add(new ReadRequestItem
                                  {
                                      RequestIndex = _swapCounter,
                                      TextureBgraReadAccess = this,
                                      Filepath = filepath,
                                      CpuAccessTexture = cpuAccessTexture,
                                      OnSuccess = onSuccess,
                                      CpuAccessTextureCount = CpuAccessTextureCount,

                                  });
        }

        return true;
    }

    /// <summary>
    /// Create several textures with a given format with CPU access to be able to read out the initial texture values
    /// </summary>
    private void PrepareCpuAccessTexturesAndConversionTextures(Texture2DDescription currentDesc)
    {
        if (_imagesWithCpuAccess.Count != 0
            && _imagesWithCpuAccess[0].Description.Width == currentDesc.Width
            && _imagesWithCpuAccess[0].Description.Height == currentDesc.Height
           )
            return;

        DisposeTextures();
        if (_readRequests.Count > 0)
        {
            Log.Debug($"Discarding {_readRequests.Count} texture frames with outdated format");
            _readRequests.Clear();
        }

        // Create read back textures
        var cpuAccessDescription = new Texture2DDescription
                                       {
                                           BindFlags = BindFlags.None,
                                           Format = _targetFormat,
                                           Width = currentDesc.Width,
                                           Height = currentDesc.Height,
                                           MipLevels = 1,
                                           SampleDescription = new SampleDescription(1, 0),
                                           Usage = ResourceUsage.Staging,
                                           OptionFlags = ResourceOptionFlags.None,
                                           CpuAccessFlags = CpuAccessFlags.Read,
                                           ArraySize = 1
                                       };

        
        
        for (var i = 0; i < CpuAccessTextureCount; ++i)
        {
            _imagesWithCpuAccess.Add(Texture2D.CreateTexture2D(cpuAccessDescription));
        }

        // Create format conversion texture
        var convertTextureDescription = new Texture2DDescription
                                            {
                                                BindFlags = BindFlags.UnorderedAccess | BindFlags.RenderTarget | BindFlags.ShaderResource,
                                                Format = _targetFormat,
                                                Width = currentDesc.Width,
                                                Height = currentDesc.Height,
                                                MipLevels = 1,
                                                SampleDescription = new SampleDescription(1, 0),
                                                Usage = ResourceUsage.Default,
                                                OptionFlags = ResourceOptionFlags.None,
                                                CpuAccessFlags = CpuAccessFlags.None,
                                                ArraySize = 1
                                            };
        _conversionTexture = Texture2D.CreateTexture2D(convertTextureDescription);
        _conversionTexture.CreateUnorderedAccessView(ref _conversionUav, 
                                                     "textureReadAccessUav");
    }

    #region conversion shader
    private  void PrepareResolveShaderResources()
    {
        if (_convertComputeShaderResource != null)
            return;

        const string sourceAddress = FileLocations.LibPackageName + ":shaders/img/ConvertFormat-cs.hlsl";
        AssetRegistry.TryResolveAddress(sourceAddress, null,out var sourcePath, out _);
        const string entryPoint = "main";
        //const string debugName = "resolve-convert-texture-format";

        _convertComputeShaderResource = ResourceManager.CreateShaderResource<ComputeShader>(sourcePath, null, () => entryPoint);
       

        if (_convertComputeShaderResource.Value == null)
            Log.Error($"Failed to initialize video conversion shader");
    }

    private  void ConvertTextureToRgba(Texture2D inputTexture)
    {
        if (_convertComputeShaderResource?.Value == null)
            return;
        
        var device = ResourceManager.Device;
        var deviceContext = device.ImmediateContext;
        var csStage = deviceContext.ComputeShader;

        // Keep previous setup
        var prevShader = csStage.Get();
        var prevUavs = csStage.GetUnorderedAccessViews(0, 1);
        var prevSrvs = csStage.GetShaderResources(0, 1);

        var convertShader = _convertComputeShaderResource.Value;
        csStage.Set(convertShader);

        const int threadNumX = 16, threadNumY = 16;
        var srv = SrvManager.GetSrvForTexture(inputTexture);
        csStage.SetShaderResource(0, srv);
        csStage.SetUnorderedAccessView(0, _conversionUav, 0);

        var dispatchCountX = (inputTexture.Description.Width / threadNumX) + 1;
        var dispatchCountY = (inputTexture.Description.Height / threadNumY) + 1;
        deviceContext.Dispatch(dispatchCountX, dispatchCountY, 1);

        // Restore prev setup
        csStage.SetUnorderedAccessView(0, prevUavs[0]);
        csStage.SetShaderResource(0, prevSrvs[0]);
        csStage.Set(prevShader);
    }

    private  Resource<ComputeShader>? _convertComputeShaderResource;
    #endregion


    
    private  int CpuAccessTextureCount => _useImmediateReadback ? 1 : DefaultCpuAccessTextureCount;
    private const int DefaultCpuAccessTextureCount = 3;
    
    private  readonly List<Texture2D> _imagesWithCpuAccess = [];

    /** Skip a certain number of images at the beginning since the
     * final content will only appear after several buffer flips*/
    public const int SkipImages = 0;

    public void ClearQueue()
    {
        _readRequests.Clear();
    }

    private void DisposeTextures()
    {
        _conversionTexture?.Dispose();

        foreach (var image in _imagesWithCpuAccess)
        {
            image.Dispose();
        }

        _imagesWithCpuAccess.Clear();
    }
    
    public void Dispose()
    {
        DisposeTextures();
        _convertComputeShaderResource?.Dispose();
        _convertComputeShaderResource = null;
        _conversionUav?.Dispose();
        _conversionUav = null;
    }
    
    private  readonly List<ReadRequestItem> _readRequests = new(3);
    private  int _swapCounter;
    private  Texture2D? _conversionTexture;
    private  UnorderedAccessView? _conversionUav;    
}