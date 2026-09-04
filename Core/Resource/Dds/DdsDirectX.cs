using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using JeremyAnsel.Media.Dds;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

// ReSharper disable InconsistentNaming


namespace T3.Core.Resource.Dds;

/// <summary>
/// Handles the resource generation for Dds files. This is basically an adaptation of
/// https://github.com/JeremyAnsel/JeremyAnsel.DirectX.Dds by Jérémy Ansel. 
/// </summary>
public static class DdsDirectX
{
    public static void CreateTexture(string fileName,
                                     Device device,
                                     DeviceContext context,
                                     out ShaderResourceView textureView)
    {
        var dds = DdsFile.FromFile(fileName);
        CreateTexture(dds, device, context, out textureView);
    }

    public static void CreateTexture(string fileName,
                                     Device device,
                                     DeviceContext context,
                                     out SharpDX.Direct3D11.Resource texture,
                                     out ShaderResourceView textureView)
    {
        var dds = DdsFile.FromFile(fileName);
        CreateTexture(dds, device, context, out texture, out textureView);
    }

    public static void CreateTexture(Stream stream,
                                     Device device,
                                     DeviceContext context,
                                     out ShaderResourceView textureView)
    {
        var dds = DdsFile.FromStream(stream);
        CreateTexture(dds, device, context, out textureView);
    }

    public static void CreateTexture(Stream stream,
                                     Device device,
                                     DeviceContext context,
                                     out SharpDX.Direct3D11.Resource texture,
                                     out ShaderResourceView textureView)
    {
        var dds = DdsFile.FromStream(stream);
        CreateTexture(dds, device, context, out texture, out textureView);
    }

    static void DisposeAndNull<T>(ref T obj) where T : class, IDisposable
    {
        obj?.Dispose();
        obj = null;
    }

    private static void CreateTexture(DdsFile dds,
                                      Device device,
                                      DeviceContext context,
                                      out ShaderResourceView textureView)
    {
        CreateTexture(dds, device, context, 0, out var texture, out textureView, out _);

        DisposeAndNull(ref texture);
    }

    public static void CreateTexture(DdsFile dds,
                                     Device device,
                                     DeviceContext context,
                                     out SharpDX.Direct3D11.Resource texture,
                                     out ShaderResourceView textureView)
    {
        CreateTexture(dds, device, context, 0, out texture, out textureView, out _);
    }

    public static void CreateTexture(DdsFile dds,
                                     Device device,
                                     DeviceContext context,
                                     int maxSize,
                                     out ShaderResourceView textureView)
    {
        CreateTexture(dds, device, context, maxSize, out var texture, out textureView, out _);
        DisposeAndNull(ref texture);
    }

    private static void CreateTexture(DdsFile dds,
                                      Device device,
                                      DeviceContext context,
                                      int maxSize,
                                      out SharpDX.Direct3D11.Resource texture,
                                      out ShaderResourceView textureView,
                                      out DdsAlphaMode alphaMode)
    {
        CreateTexture(
                      dds,
                      device,
                      context,
                      maxSize,
                      ResourceUsage.Default, // .Default,
                      BindFlags.ShaderResource,
                      CpuAccessFlags.None,
                      ResourceOptionFlags.None,
                      false,
                      out texture,
                      out textureView,
                      out alphaMode);
    }

    private static void CreateTexture(DdsFile dds,
                                      Device device,
                                      DeviceContext context,
                                      int maxSize,
                                      ResourceUsage usage,
                                      BindFlags bindOptions,
                                      CpuAccessFlags cpuAccessOptions,
                                      ResourceOptionFlags miscOptions,
                                      bool forceSRGB,
                                      out SharpDX.Direct3D11.Resource texture,
                                      out ShaderResourceView textureView,
                                      out DdsAlphaMode alphaMode)
    {
        if (dds == null)
        {
            throw new ArgumentNullException(nameof(dds));
        }

        if (device == null)
        {
            throw new ArgumentNullException(nameof(device));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        CreateTextureFromDDS(
                             device,
                             context,
                             dds,
                             dds.Data,
                             maxSize,
                             usage,
                             bindOptions,
                             cpuAccessOptions,
                             miscOptions,
                             forceSRGB,
                             out texture,
                             out textureView);

        alphaMode = dds.AlphaMode;
    }

    private static bool FillInitData(int width,
                                     int height,
                                     int depth,
                                     int mipCount,
                                     int arraySize,
                                     Format format,
                                     int maxSize,
                                     byte[] bitData,
                                     out int tWidth,
                                     out int tHeight,
                                     out int tDepth,
                                     out int skipMip,
                                     out D3D11SubResourceData[] initData)
    {
        skipMip = 0;
        tWidth = 0;
        tHeight = 0;
        tDepth = 0;
        initData = new D3D11SubResourceData[mipCount * arraySize];

        var pSrcBits = 0;
        var index = 0;

        for (var j = 0; j < arraySize; j++)
        {
            var w = width;
            var h = height;
            var d = depth;

            for (var i = 0; i < mipCount; i++)
            {
                DdsHelpers.GetSurfaceInfo(w, h, (DdsFormat)format, out var NumBytes, out var RowBytes, out _);

                if ((mipCount <= 1) || maxSize == 0 || (w <= maxSize && h <= maxSize && d <= maxSize))
                {
                    if (tWidth == 0)
                    {
                        tWidth = w;
                        tHeight = h;
                        tDepth = d;
                    }

                    var dataLength = NumBytes * d;
                    var data = new byte[dataLength];
                    Array.Copy(bitData, pSrcBits, data, 0, dataLength);

                    initData[index] = new D3D11SubResourceData(data, (uint)RowBytes, (uint)NumBytes);
                    index++;
                }
                else if (j == 0)
                {
                    // Count number of skipped mipmaps (first item only)
                    skipMip++;
                }

                pSrcBits += NumBytes * d;

                w >>= 1;
                h >>= 1;
                d >>= 1;

                if (w == 0)
                {
                    w = 1;
                }

                if (h == 0)
                {
                    h = 1;
                }

                if (d == 0)
                {
                    d = 1;
                }
            }
        }

        return index > 0;
    }

    private static void CreateD3DResources(Device device,
                                           ResourceDimension resDim,
                                           int width,
                                           int height,
                                           int depth,
                                           int mipCount,
                                           int arraySize,
                                           Format format,
                                           ResourceUsage usage,
                                           BindFlags bindFlags,
                                           CpuAccessFlags cpuAccessFlags,
                                           ResourceOptionFlags miscFlags,
                                           bool forceSRGB,
                                           bool isCubeMap,
                                           D3D11SubResourceData[] initData,
                                           out SharpDX.Direct3D11.Resource texture,
                                           out ShaderResourceView textureView)
    {
        texture = null;
        textureView = null;

        if (forceSRGB)
        {
            format = (Format)DdsHelpers.MakeSrgb((DdsFormat)format);
        }

        switch (resDim)
        {
            case ResourceDimension.Texture1D:
            {
                var tex1dDescription = new Texture1DDescription
                                           {
                                               Format = format,
                                               Width = width,
                                               ArraySize = arraySize,
                                               MipLevels = mipCount,
                                               BindFlags = bindFlags,
                                               Usage = usage,
                                               CpuAccessFlags = cpuAccessFlags,
                                               OptionFlags = miscFlags & ~ResourceOptionFlags.TextureCube,
                                           };

                // We keep these for reference...
                    
                // texture = device.CreateTexture1D(desc, initData);  // Jeremy implementation with device helper method
                // texture = new Texture1D(device, tex1dDescription, dataRectangles);  // Failed because Texture1d doesn't support DataRectangles constructor
                // var tex2d = new Texture2D(device, new Texture2DDescription(), dataRectangles); // Reference from DataRectangles with Texture2d

                if (initData == null)
                {
                    texture = new Texture1D(device, tex1dDescription);
                }
                else {
                    var dataBoxes = CreateDataBoxesFromSubresourceData(initData);
                    texture = new Texture1D(device, tex1dDescription, dataBoxes);
                }

                try
                {
                    var SRVDesc = new ShaderResourceViewDescription
                                      {
                                          Format = format
                                      };

                    if (arraySize > 1)
                    {
                        SRVDesc.Dimension = ShaderResourceViewDimension.Texture1DArray;
                        SRVDesc.Texture1DArray = new ShaderResourceViewDescription.Texture1DArrayResource
                                                     {
                                                         MipLevels = (mipCount == 0) ? -1 : tex1dDescription.MipLevels,
                                                         ArraySize = arraySize
                                                     };
                    }
                    else
                    {
                        SRVDesc.Dimension = ShaderResourceViewDimension.Texture1D;
                        SRVDesc.Texture1D = new ShaderResourceViewDescription.Texture1DResource
                                                {
                                                    MipLevels = (mipCount == 0) ? -1 : tex1dDescription.MipLevels
                                                };
                    }

                    textureView = new ShaderResourceView(device, texture, SRVDesc);
                }
                catch
                {
                    DisposeAndNull(ref texture);
                    throw;
                }

                break;
            }

            case ResourceDimension.Texture2D:
            {
                var desc = new Texture2DDescription
                               {
                                   Format = format,
                                   Width = width,
                                   Height = height,
                                   ArraySize = arraySize,
                                   MipLevels = mipCount,
                                   BindFlags = bindFlags,
                                   Usage = usage,
                                   CpuAccessFlags = cpuAccessFlags,
                                   OptionFlags = ResourceOptionFlags.None,
                                   SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
                               };

                if (isCubeMap)
                {
                    desc.OptionFlags = miscFlags | ResourceOptionFlags.TextureCube;
                }
                else
                {
                    desc.OptionFlags = miscFlags & ~ResourceOptionFlags.TextureCube;
                }

                if (format == Format.BC1_UNorm || format == Format.BC2_UNorm || format == Format.BC3_UNorm)
                {
                    if ((width & 3) != 0 || (height & 3) != 0)
                    {
                        desc.Width = (int)((width + 3) & ~3U);
                        desc.Height = (int)((height + 3) & ~3U);
                    }
                }

                if (initData == null)
                {
                    desc.OptionFlags = ResourceOptionFlags.None;
                    texture = new Texture2D(device, desc);
                }
                else
                {
                    var dataBoxes = CreateDataBoxesFromSubresourceData(initData);
                    texture = new Texture2D(device, desc, dataBoxes);
                    //texture = device.CreateTexture2D(desc, initData);
                }
                    
                try
                {
                    var SRVDesc = new ShaderResourceViewDescription
                                      {
                                          Format = format
                                      };

                    if (isCubeMap)
                    {
                        if (arraySize > 6)
                        {
                            SRVDesc.Dimension = ShaderResourceViewDimension.TextureCubeArray;
                            SRVDesc.TextureCubeArray = new ShaderResourceViewDescription.TextureCubeArrayResource
                                                           {
                                                               MipLevels = (mipCount == 0) ? -1 : desc.MipLevels,
                                                               // Earlier we set arraySize to (NumCubes * 6)
                                                               CubeCount = arraySize / 6
                                                           };
                        }
                        else
                        {
                            SRVDesc.Dimension = ShaderResourceViewDimension.TextureCube;
                            SRVDesc.TextureCube = new ShaderResourceViewDescription.TextureCubeResource
                                                      {
                                                          MipLevels = (mipCount == 0) ? -1 : desc.MipLevels,
                                                      };
                        }
                    }
                    else if (arraySize > 1)
                    {
                        SRVDesc.Dimension = ShaderResourceViewDimension.Texture2DArray;
                        SRVDesc.Texture2DArray = new ShaderResourceViewDescription.Texture2DArrayResource
                                                     {
                                                         MipLevels = (mipCount == 0) ? -1 : desc.MipLevels,
                                                         ArraySize = arraySize
                                                     };
                    }
                    else
                    {
                        SRVDesc.Dimension = ShaderResourceViewDimension.Texture2D;
                        SRVDesc.Texture2D = new ShaderResourceViewDescription.Texture2DResource
                                                {
                                                    MipLevels = (mipCount == 0) ? -1 : desc.MipLevels,
                                                };
                    }

                    textureView = new ShaderResourceView(device, texture, SRVDesc);
                }
                catch
                {
                    DisposeAndNull(ref texture);
                    throw;
                }

                break;
            }

            case ResourceDimension.Texture3D:
            {
                var desc = new Texture3DDescription
                               {
                                   Format = format,
                                   Width = width,
                                   Height = height,
                                   Depth = depth,
                                   MipLevels = mipCount,
                                   BindFlags = bindFlags,
                                   Usage = usage,
                                   CpuAccessFlags = cpuAccessFlags,
                                   OptionFlags = miscFlags & ~ResourceOptionFlags.TextureCube,
                               };

                if (initData == null)
                {
                    texture = new Texture3D(device, desc);
                }
                else
                {
                    var dataBoxes = CreateDataBoxesFromSubresourceData(initData);
                    texture = new Texture3D(device, desc, dataBoxes);
                    //texture = device.CreateTexture3D(desc, initData);
                }

                try
                {
                    var SRVDesc = new ShaderResourceViewDescription
                                      {
                                          Format = format,
                                          Dimension = ShaderResourceViewDimension.Texture3D,
                                          Texture3D = new ShaderResourceViewDescription.Texture3DResource
                                                          {
                                                              MipLevels = (mipCount == 0) ? -1 : desc.MipLevels,
                                                          }
                                      };

                    textureView = new ShaderResourceView(device, texture, SRVDesc);
                }
                catch
                {
                    DisposeAndNull(ref texture);
                    throw;
                }

                break;
            }
        }
    }

    private static DataBox[] CreateDataBoxesFromSubresourceData(D3D11SubResourceData[] initData)
    {
        var dataBoxes = new DataBox[initData.Length];
        for (var dataIndex = 0; dataIndex < initData.Length; dataIndex++)
        {
            var dataPointer = Marshal.UnsafeAddrOfPinnedArrayElement(initData[dataIndex].Data, 0);
            // dataRectangles[dataIndex] = new DataRectangle(dataPointer, (int)initData[0].Pitch);

            dataBoxes[dataIndex] = new DataBox
                                       {
                                           DataPointer = dataPointer,
                                           RowPitch = (int)initData[dataIndex].Pitch,
                                           SlicePitch = (int)initData[dataIndex].SlicePitch
                                       };
        }

        return dataBoxes;
    }

    private static bool IsBitMask(DdsPixelFormat ddpf, uint r, uint g, uint b, uint a)
    {
        return ddpf.RedBitMask == r && ddpf.GreenBitMask == g && ddpf.BlueBitMask == b && ddpf.AlphaBitMask == a;
    }

    [SuppressMessage("Microsoft.Maintainability", "CA1505:AvoidUnmaintainableCode")]
    [SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
    [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
    private static void CreateTextureFromDDS(Device device,
                                             DeviceContext context,
                                             DdsFile dds,
                                             byte[] bitData,
                                             int maxSize,
                                             ResourceUsage usage,
                                             BindFlags bindOptions,
                                             CpuAccessFlags cpuAccessOptions,
                                             ResourceOptionFlags miscOptions,
                                             bool forceSRGB,
                                             out SharpDX.Direct3D11.Resource texture,
                                             out ShaderResourceView textureView)
    {
        var width = dds.Width;
        var height = dds.Height;
        var depth = dds.Depth;

        var resDim = (ResourceDimension)dds.ResourceDimension;
        var arraySize = Math.Max(1, dds.ArraySize);
        var format = (Format)dds.Format;
        var isCubeMap = false;

        if (dds.Format == DdsFormat.Unknown)
        {
            if (dds.PixelFormat.RgbBitCount == 32)
            {
                if (IsBitMask(dds.PixelFormat, 0x000000ff, 0x0000ff00, 0x00ff0000, 0x00000000))
                {
                    format = Format.B8G8R8X8_UNorm;
                    var length = bitData.Length / 4;
                    var bytes = new byte[length * 4];

                    for (var i = 0; i < length; i++)
                    {
                        bytes[i * 4 + 0] = bitData[i * 4 + 2];
                        bytes[i * 4 + 1] = bitData[i * 4 + 1];
                        bytes[i * 4 + 2] = bitData[i * 4 + 0];
                    }

                    bitData = bytes;
                }
            }
            else if (dds.PixelFormat.RgbBitCount == 24)
            {
                if (IsBitMask(dds.PixelFormat, 0x00ff0000, 0x0000ff00, 0x000000ff, 0x00000000))
                {
                    format = Format.B8G8R8X8_UNorm;
                    var length = bitData.Length / 3;
                    var bytes = new byte[length * 4];

                    for (var i = 0; i < length; i++)
                    {
                        bytes[i * 4 + 0] = bitData[i * 3 + 0];
                        bytes[i * 4 + 1] = bitData[i * 3 + 1];
                        bytes[i * 4 + 2] = bitData[i * 3 + 2];
                    }

                    bitData = bytes;
                }
            }
        }

        var mipCount = Math.Max(1, dds.MipmapCount);

        switch (format)
        {
            case Format.AI44:
            case Format.IA44:
            case Format.P8:
            case Format.A8P8:
                throw new NotSupportedException(format + " format is not supported.");

            default:
                if (DdsHelpers.GetBitsPerPixel((DdsFormat)format) == 0)
                {
                    throw new NotSupportedException(format + " format is not supported.");
                }

                break;
        }

        switch (resDim)
        {
            case ResourceDimension.Texture1D:
                // D3DX writes 1D textures with a fixed Height of 1
                if ((dds.Options & DdsOptions.Height) != 0 && height != 1)
                {
                    throw new InvalidDataException();
                }

                height = 1;
                depth = 1;
                break;

            case ResourceDimension.Texture2D:
                if ((dds.ResourceMiscOptions & DdsResourceMiscOptions.TextureCube) != 0)
                {
                    arraySize *= 6;
                    isCubeMap = true;
                }

                depth = 1;
                break;

            case ResourceDimension.Texture3D:
                if ((dds.Options & DdsOptions.Depth) == 0)
                {
                    throw new InvalidDataException();
                }

                if (arraySize > 1)
                {
                    throw new NotSupportedException();
                }

                break;

            default:
                if ((dds.Options & DdsOptions.Depth) != 0)
                {
                    resDim = ResourceDimension.Texture3D;
                }
                else
                {
                    if ((dds.Caps2 & DdsAdditionalCaps.CubeMap) != 0)
                    {
                        // We require all six faces to be defined
                        if ((dds.Caps2 & DdsAdditionalCaps.CubeMapAllFaces) != DdsAdditionalCaps.CubeMapAllFaces)
                        {
                            throw new NotSupportedException();
                        }

                        arraySize = 6;
                        isCubeMap = true;
                    }

                    depth = 1;
                    resDim = ResourceDimension.Texture2D;

                    // Note there's no way for a legacy Direct3D 9 DDS to express a '1D' texture
                }

                break;
        }

        if ((miscOptions & ResourceOptionFlags.TextureCube) != 0
            && resDim == ResourceDimension.Texture2D
            && (arraySize % 6 == 0))
        {
            isCubeMap = true;
        }

        // Bound sizes (for security purposes we don't trust DDS file metadata larger than the D3D 11.x hardware requirements)
        if (mipCount > D3D11Constants.ReqMipLevels)
        {
            throw new NotSupportedException();
        }

        switch (resDim)
        {
            case ResourceDimension.Texture1D:
                if (arraySize > D3D11Constants.ReqTexture1DArrayAxisDimension
                    || width > D3D11Constants.ReqTexture1DDimension)
                {
                    throw new NotSupportedException();
                }

                break;

            case ResourceDimension.Texture2D:
                if (isCubeMap)
                {
                    // This is the right bound because we set arraySize to (NumCubes*6) above
                    if (arraySize > D3D11Constants.ReqTexture2DArrayAxisDimension
                        || width > D3D11Constants.ReqTextureCubeDimension
                        || height > D3D11Constants.ReqTextureCubeDimension)
                    {
                        throw new NotSupportedException();
                    }
                }
                else if (arraySize > D3D11Constants.ReqTexture2DArrayAxisDimension
                         || width > D3D11Constants.ReqTexture2DDimension
                         || height > D3D11Constants.ReqTexture2DDimension)
                {
                    throw new NotSupportedException();
                }

                break;

            case ResourceDimension.Texture3D:
                if (arraySize > 1
                    || width > D3D11Constants.ReqTexture3DDimension
                    || height > D3D11Constants.ReqTexture3DDimension
                    || depth > D3D11Constants.ReqTexture3DDimension)
                {
                    throw new NotSupportedException();
                }

                break;

            default:
                throw new NotSupportedException();
        }

        var autogen = false;

        if (mipCount == 1)
        {
            // See if format is supported for auto-gen mipmaps (varies by feature level)
            // if (!device.CheckFormatSupport(format, out FormatSupport fmtSupport)
            //     && (fmtSupport & FormatSupport.MipAutogen) != 0)
            var fmtSupport = device.CheckFormatSupport(format);
            if ((fmtSupport & FormatSupport.MipAutogen) != 0)
            {
                // 10level9 feature levels do not support auto-gen mipgen for volume textures
                if (resDim != ResourceDimension.Texture3D
                    || device.FeatureLevel >= FeatureLevel.Level_10_0)
                {
                    autogen = true;
                }
            }
        }

        if (autogen)
        {
            // Create texture with auto-generated mipmaps
            CreateD3DResources(
                               device,
                               resDim,
                               width,
                               height,
                               depth,
                               0,
                               arraySize,
                               format,
                               usage,
                               bindOptions | BindFlags.RenderTarget,
                               cpuAccessOptions,
                               miscOptions | ResourceOptionFlags.GenerateMipMaps,
                               forceSRGB,
                               isCubeMap,
                               null,
                               out texture,
                               out textureView);

            try
            {
                DdsHelpers.GetSurfaceInfo(width, height, (DdsFormat)format, out var numBytes, out var rowBytes, out var numRows);

                if (numBytes > bitData.Length)
                {
                    throw new EndOfStreamException();
                }

                var desc = textureView.Description;
                var mipLevels = 1;

                switch (desc.Dimension)
                {
                    case ShaderResourceViewDimension.Texture1D:
                        mipLevels = desc.Texture1D.MipLevels;
                        break;

                    case ShaderResourceViewDimension.Texture1DArray:
                        mipLevels = desc.Texture1DArray.MipLevels;
                        break;

                    case ShaderResourceViewDimension.Texture2D:
                        mipLevels = desc.Texture2D.MipLevels;
                        break;

                    case ShaderResourceViewDimension.Texture2DArray:
                        mipLevels = desc.Texture2DArray.MipLevels;
                        break;

                    case ShaderResourceViewDimension.TextureCube:
                        mipLevels = desc.TextureCube.MipLevels;
                        break;

                    case ShaderResourceViewDimension.TextureCubeArray:
                        mipLevels = desc.TextureCubeArray.MipLevels;
                        break;

                    case ShaderResourceViewDimension.Texture3D:
                        mipLevels = desc.Texture3D.MipLevels;
                        break;

                    default:
                        throw new InvalidDataException();
                }

                if (arraySize > 1)
                {
                    var pSrcBits = 0;

                    for (var item = 0; item < (uint)arraySize; item++)
                    {
                        if (pSrcBits + numBytes > bitData.Length)
                        {
                            throw new EndOfStreamException();
                        }

                        var data = new byte[numBytes];
                        Array.Copy(bitData, pSrcBits, data, 0, numBytes);

                        var res = SharpDX.Direct3D11.Resource.CalculateSubResourceIndex(0, item, mipLevels);
                            
                        var dataPointer = Marshal.UnsafeAddrOfPinnedArrayElement(data, 0);
                        context.UpdateSubresource(texture, res, null, dataPointer, rowBytes, numBytes);

                        pSrcBits += numBytes;
                    }
                }
                else
                {
                    var dataPointer = Marshal.UnsafeAddrOfPinnedArrayElement(bitData, 0);
                    context.UpdateSubresource(texture, 0, null, dataPointer, rowBytes, numBytes);
                }

                context.GenerateMips(textureView);
            }
            catch
            {
                DisposeAndNull(ref textureView);
                DisposeAndNull(ref texture);
                throw;
            }
        }
        else
        {
            // Create the texture

            if (!FillInitData(
                              width,
                              height,
                              depth,
                              mipCount,
                              arraySize,
                              format,
                              maxSize,
                              bitData,
                              out var twidth,
                              out var theight,
                              out var tdepth,
                              out var skipMip,
                              out var initData))
            {
                throw new InvalidDataException();
            }

            try
            {
                CreateD3DResources(
                                   device,
                                   resDim,
                                   twidth,
                                   theight,
                                   tdepth,
                                   mipCount - skipMip,
                                   arraySize,
                                   format,
                                   usage,
                                   bindOptions,
                                   cpuAccessOptions,
                                   miscOptions,
                                   forceSRGB,
                                   isCubeMap,
                                   initData,
                                   out texture,
                                   out textureView);
            }
            catch
            {
                if (maxSize == 0 && mipCount > 1)
                {
                    // Retry with a maxsize determined by feature level
                    switch (device.FeatureLevel)
                    {
                        case FeatureLevel.Level_9_1:
                        case FeatureLevel.Level_9_2:
                            if (isCubeMap)
                            {
                                maxSize = (int)D3D11Constants.FeatureLevel91ReqTextureCubeDimension;
                            }
                            else
                            {
                                maxSize = resDim == ResourceDimension.Texture3D
                                              ? (int)D3D11Constants.FeatureLevel91ReqTexture3DDimension
                                              : (int)D3D11Constants.FeatureLevel91ReqTexture2DDimension;
                            }

                            break;

                        case FeatureLevel.Level_9_3:
                            maxSize = resDim == ResourceDimension.Texture3D
                                          ? (int)D3D11Constants.FeatureLevel91ReqTexture3DDimension
                                          : (int)D3D11Constants.FeatureLevel93ReqTexture2DDimension;
                            break;

                        case FeatureLevel.Level_10_0:
                        case FeatureLevel.Level_10_1:
                            maxSize = resDim == ResourceDimension.Texture3D
                                          ? (int)D3D11Constants.D3D10ReqTexture3DDimension
                                          : (int)D3D11Constants.D3D10ReqTexture2DDimension;
                            break;

                        default:
                            maxSize = resDim == ResourceDimension.Texture3D
                                          ? (int)D3D11Constants.ReqTexture3DDimension
                                          : (int)D3D11Constants.ReqTexture2DDimension;
                            break;
                    }

                    if (!FillInitData(
                                      width,
                                      height,
                                      depth,
                                      mipCount,
                                      arraySize,
                                      format,
                                      maxSize,
                                      bitData,
                                      out twidth,
                                      out theight,
                                      out tdepth,
                                      out skipMip,
                                      out initData))
                    {
                        throw new InvalidDataException();
                    }

                    CreateD3DResources(
                                       device,
                                       resDim,
                                       twidth,
                                       theight,
                                       tdepth,
                                       mipCount - skipMip,
                                       arraySize,
                                       format,
                                       usage,
                                       bindOptions,
                                       cpuAccessOptions,
                                       miscOptions,
                                       forceSRGB,
                                       isCubeMap,
                                       initData,
                                       out texture,
                                       out textureView);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}