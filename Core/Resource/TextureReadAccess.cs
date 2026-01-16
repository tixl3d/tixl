#nullable enable

using System;
using System.Collections.Generic;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using T3.Core.Logging;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Core.Resource;

/// <summary>
/// Simple texture read access with multiple cycle buffers 
/// </summary>
public sealed class TextureReadAccess : IDisposable
{
    public sealed class ReadRequestItem
    {
        internal int RequestIndex;
        public required TextureReadAccess TextureReadAccess;
        public required OnReadComplete OnSuccess;
        public required Texture2D CpuAccessTexture;

        internal bool IsReady => RequestIndex == TextureReadAccess._swapCounter - (CpuAccessTextureCount - 2);
        internal bool IsObsolete => RequestIndex < TextureReadAccess._swapCounter - (CpuAccessTextureCount - 2);
    }

    /// <summary>
    /// Saving a screenshot will take several frames because it takes a while until the frames are
    /// downloaded from the gpu. <see cref="Update"/> needs to be called once a frame.
    /// </summary>
    public void Update()
    {
        _swapCounter++;

        while (_readRequests.Count > 0 && _readRequests[0].IsObsolete)
        {
            Log.Debug("Remove obsolete");
            _readRequests.RemoveAt(0);
        }

        if (_readRequests.Count == 0 || !_readRequests[0].IsReady)
            return;

        var completedRequest = _readRequests[0];
        var context = ResourceManager.Device.ImmediateContext;

        var dataBox = context.MapSubresource(
                                             completedRequest.CpuAccessTexture,
                                             0,
                                             MapMode.Read,
                                             SharpDX.Direct3D11.MapFlags.None
                                            );

        try
        {
            completedRequest.OnSuccess(completedRequest);
        }
        finally
        {
            context.UnmapSubresource(completedRequest.CpuAccessTexture, 0);
            _readRequests.RemoveAt(0);
        }
    }


    public delegate void OnReadComplete(ReadRequestItem cpuAccessTexture);
    
    /// <summary>
    /// Convert into BGRA and initiate the readback process.
    /// It will take several frames until the texture is accessible and the callback is called.
    /// </summary>
    public bool InitiateReadAndConvert(Texture2D originalTexture, OnReadComplete onSuccess, string? filepath=null)
    {
        if (originalTexture == null! || originalTexture.IsDisposed)
            return false;

        PrepareCpuAccessTextures(originalTexture.Description);
        
        // Copy the original texture to a readable image
        var immediateContext = ResourceManager.Device.ImmediateContext;
        var cpuAccessTexture = _imagesWithCpuAccess[_swapCounter % CpuAccessTextureCount];
        immediateContext.CopyResource(originalTexture, cpuAccessTexture);
        //immediateContext.UnmapSubresource(cpuAccessTexture, 0);

        _readRequests.Add(new ReadRequestItem
                              {
                                  TextureReadAccess = this,
                                  RequestIndex = _swapCounter,
                                  CpuAccessTexture = cpuAccessTexture,
                                  OnSuccess = onSuccess,
                              });

        return true;
    }

    /// <summary>
    /// Create several textures with a given format with CPU access to be able to read out the initial texture values
    /// </summary>
    private void PrepareCpuAccessTextures(Texture2DDescription currentDesc)
    {
        if (_imagesWithCpuAccess.Count != 0
            && _imagesWithCpuAccess[0].Description.Width == currentDesc.Width
            && _imagesWithCpuAccess[0].Description.Height == currentDesc.Height
            && _imagesWithCpuAccess[0].Description.Format == currentDesc.Format
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
                                           Format = currentDesc.Format,
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
    }
    
    
    private const int CpuAccessTextureCount = 3;
    private  readonly List<Texture2D> _imagesWithCpuAccess = new(CpuAccessTextureCount);
    
    public void ClearQueue()
    {
        _readRequests.Clear();
    }

    private void DisposeTextures()
    {
        foreach (var image in _imagesWithCpuAccess)
        {
            image.Dispose();
        }

        _imagesWithCpuAccess.Clear();
    }
    
    public void Dispose()
    {
        DisposeTextures();
    }
    
    private  readonly List<ReadRequestItem> _readRequests = new(3);
    private  int _swapCounter;
}