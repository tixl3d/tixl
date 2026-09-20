using T3.Graphics;

namespace T3.Graphics.Compat;

/// <summary>
/// Fills in the view descriptions D3D11 derives from the resource when none is given, and reduces the
/// per-dimension union to the backend's flat view description.
/// </summary>
internal static class ViewDescriptions
{
    internal static ShaderResourceViewDescription DefaultShaderResourceView(Resource resource)
    {
        switch (resource)
        {
            case Texture2D texture:
            {
                var mips = Math.Max(1, texture.Description.MipLevels);
                var slices = Math.Max(1, texture.Description.ArraySize);
                var isCube = (texture.Description.OptionFlags & ResourceOptionFlags.TextureCube) != 0;

                return new ShaderResourceViewDescription
                           {
                               Format = texture.Description.Format,
                               Dimension = isCube
                                               ? ShaderResourceViewDimension.TextureCube
                                               : slices > 1
                                                   ? ShaderResourceViewDimension.Texture2DArray
                                                   : ShaderResourceViewDimension.Texture2D,
                               Texture2D = new ShaderResourceViewDescription.Texture2DResource { MipLevels = mips },
                               TextureCube = new ShaderResourceViewDescription.TextureCubeResource { MipLevels = mips },
                               Texture2DArray = new ShaderResourceViewDescription.Texture2DArrayResource { MipLevels = mips, ArraySize = slices },
                           };
            }

            case Texture3D texture:
                return new ShaderResourceViewDescription
                           {
                               Format = texture.Description.Format,
                               Dimension = ShaderResourceViewDimension.Texture3D,
                               Texture3D = new ShaderResourceViewDescription.Texture3DResource
                                               { MipLevels = Math.Max(1, texture.Description.MipLevels) },
                           };

            case Texture1D texture:
                return new ShaderResourceViewDescription
                           {
                               Format = texture.Description.Format,
                               Dimension = texture.Description.ArraySize > 1
                                               ? ShaderResourceViewDimension.Texture1DArray
                                               : ShaderResourceViewDimension.Texture1D,
                               Texture1D = new ShaderResourceViewDescription.Texture1DResource
                                               { MipLevels = Math.Max(1, texture.Description.MipLevels) },
                               Texture1DArray = new ShaderResourceViewDescription.Texture1DArrayResource
                                                    {
                                                        MipLevels = Math.Max(1, texture.Description.MipLevels),
                                                        ArraySize = Math.Max(1, texture.Description.ArraySize),
                                                    },
                           };

            case Buffer buffer:
                return new ShaderResourceViewDescription
                           {
                               Dimension = ShaderResourceViewDimension.ExtendedBuffer,
                               BufferEx = new ShaderResourceViewDescription.ExtendedBufferResource { ElementCount = ElementCount(buffer) },
                           };

            default:
                throw new ArgumentException($"Cannot derive a shader resource view for {resource.GetType().Name}");
        }
    }

    internal static RenderTargetViewDescription DefaultRenderTargetView(Resource resource)
    {
        return resource switch
                   {
                       Texture2D texture => new RenderTargetViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = texture.Description.ArraySize > 1
                                                                    ? RenderTargetViewDimension.Texture2DArray
                                                                    : RenderTargetViewDimension.Texture2D,
                                                    Texture2DArray = new RenderTargetViewDescription.Texture2DArrayResource
                                                                         { ArraySize = Math.Max(1, texture.Description.ArraySize) },
                                                },
                       Texture3D texture => new RenderTargetViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = RenderTargetViewDimension.Texture3D,
                                                    Texture3D = new RenderTargetViewDescription.Texture3DResource
                                                                    { DepthSliceCount = Math.Max(1, texture.Description.Depth) },
                                                },
                       _ => throw new ArgumentException($"Cannot derive a render target view for {resource.GetType().Name}"),
                   };
    }

    internal static DepthStencilViewDescription DefaultDepthStencilView(Resource resource)
    {
        return resource switch
                   {
                       Texture2D texture => new DepthStencilViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = texture.Description.ArraySize > 1
                                                                    ? DepthStencilViewDimension.Texture2DArray
                                                                    : DepthStencilViewDimension.Texture2D,
                                                    Texture2DArray = new DepthStencilViewDescription.Texture2DArrayResource
                                                                         { ArraySize = Math.Max(1, texture.Description.ArraySize) },
                                                },
                       _ => throw new ArgumentException($"Cannot derive a depth stencil view for {resource.GetType().Name}"),
                   };
    }

    internal static UnorderedAccessViewDescription DefaultUnorderedAccessView(Resource resource)
    {
        return resource switch
                   {
                       Texture2D texture => new UnorderedAccessViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = texture.Description.ArraySize > 1
                                                                    ? UnorderedAccessViewDimension.Texture2DArray
                                                                    : UnorderedAccessViewDimension.Texture2D,
                                                    Texture2DArray = new UnorderedAccessViewDescription.Texture2DArrayResource
                                                                         { ArraySize = Math.Max(1, texture.Description.ArraySize) },
                                                },
                       Texture3D texture => new UnorderedAccessViewDescription
                                                {
                                                    Format = texture.Description.Format,
                                                    Dimension = UnorderedAccessViewDimension.Texture3D,
                                                    Texture3D = new UnorderedAccessViewDescription.Texture3DResource
                                                                    { WSize = Math.Max(1, texture.Description.Depth) },
                                                },
                       Buffer buffer => new UnorderedAccessViewDescription
                                            {
                                                Dimension = UnorderedAccessViewDimension.Buffer,
                                                Buffer = new UnorderedAccessViewDescription.BufferResource { ElementCount = ElementCount(buffer) },
                                            },
                       _ => throw new ArgumentException($"Cannot derive an unordered access view for {resource.GetType().Name}"),
                   };
    }

    internal static TextureViewDescription ToTextureView(in ShaderResourceViewDescription description)
    {
        return description.Dimension switch
                   {
                       ShaderResourceViewDimension.Texture1D
                           => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1D.MostDetailedMip,
                                      description.Texture1D.MipLevels, 0, 1),
                       ShaderResourceViewDimension.Texture1DArray
                           => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1DArray.MostDetailedMip,
                                      description.Texture1DArray.MipLevels, description.Texture1DArray.FirstArraySlice,
                                      description.Texture1DArray.ArraySize),
                       ShaderResourceViewDimension.Texture3D
                           => Flatten(description.Format, TextureDimension.Texture3D, description.Texture3D.MostDetailedMip,
                                      description.Texture3D.MipLevels, 0, 1),
                       ShaderResourceViewDimension.TextureCube
                           => Flatten(description.Format, TextureDimension.TextureCube, description.TextureCube.MostDetailedMip,
                                      description.TextureCube.MipLevels, 0, 6),
                       ShaderResourceViewDimension.TextureCubeArray
                           => Flatten(description.Format, TextureDimension.TextureCube, description.TextureCubeArray.MostDetailedMip,
                                      description.TextureCubeArray.MipLevels, description.TextureCubeArray.First2DArrayFace,
                                      Math.Max(6, description.TextureCubeArray.CubeCount * 6)),
                       ShaderResourceViewDimension.Texture2DArray or ShaderResourceViewDimension.Texture2DMultisampledArray
                           => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2DArray.MostDetailedMip,
                                      description.Texture2DArray.MipLevels, description.Texture2DArray.FirstArraySlice,
                                      description.Texture2DArray.ArraySize),
                       _ => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2D.MostDetailedMip,
                                    description.Texture2D.MipLevels, 0, 1),
                   };
    }

    internal static TextureViewDescription ToTextureView(in RenderTargetViewDescription description)
    {
        return description.Dimension switch
                   {
                       RenderTargetViewDimension.Texture1D
                           => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1D.MipSlice, 1, 0, 1),
                       RenderTargetViewDimension.Texture1DArray
                           => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1DArray.MipSlice, 1,
                                      description.Texture1DArray.FirstArraySlice, description.Texture1DArray.ArraySize),
                       RenderTargetViewDimension.Texture3D
                           => Flatten(description.Format, TextureDimension.Texture3D, description.Texture3D.MipSlice, 1,
                                      description.Texture3D.FirstDepthSlice, description.Texture3D.DepthSliceCount),
                       RenderTargetViewDimension.Texture2DArray or RenderTargetViewDimension.Texture2DMultisampledArray
                           => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2DArray.MipSlice, 1,
                                      description.Texture2DArray.FirstArraySlice, description.Texture2DArray.ArraySize),
                       _ => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2D.MipSlice, 1, 0, 1),
                   };
    }

    internal static TextureViewDescription ToTextureView(in DepthStencilViewDescription description)
    {
        return description.Dimension switch
                   {
                       DepthStencilViewDimension.Texture1D
                           => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1D.MipSlice, 1, 0, 1),
                       DepthStencilViewDimension.Texture1DArray
                           => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1DArray.MipSlice, 1,
                                      description.Texture1DArray.FirstArraySlice, description.Texture1DArray.ArraySize),
                       DepthStencilViewDimension.Texture2DArray or DepthStencilViewDimension.Texture2DMultisampledArray
                           => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2DArray.MipSlice, 1,
                                      description.Texture2DArray.FirstArraySlice, description.Texture2DArray.ArraySize),
                       _ => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2D.MipSlice, 1, 0, 1),
                   };
    }

    internal static TextureViewDescription ToTextureView(in UnorderedAccessViewDescription description)
    {
        return description.Dimension switch
                   {
                       UnorderedAccessViewDimension.Texture1D
                           => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1D.MipSlice, 1, 0, 1),
                       UnorderedAccessViewDimension.Texture1DArray
                           => Flatten(description.Format, TextureDimension.Texture1D, description.Texture1DArray.MipSlice, 1,
                                      description.Texture1DArray.FirstArraySlice, description.Texture1DArray.ArraySize),
                       UnorderedAccessViewDimension.Texture3D
                           => Flatten(description.Format, TextureDimension.Texture3D, description.Texture3D.MipSlice, 1,
                                      description.Texture3D.FirstWSlice, description.Texture3D.WSize),
                       UnorderedAccessViewDimension.Texture2DArray
                           => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2DArray.MipSlice, 1,
                                      description.Texture2DArray.FirstArraySlice, description.Texture2DArray.ArraySize),
                       _ => Flatten(description.Format, TextureDimension.Texture2D, description.Texture2D.MipSlice, 1, 0, 1),
                   };
    }

    /// <summary>Views of a buffer bind a byte range; the element size comes from the buffer's stride.</summary>
    internal static (int Offset, int Size) BufferRange(Buffer buffer, int firstElement, int elementCount)
    {
        var stride = buffer.Description.StructureByteStride > 0 ? buffer.Description.StructureByteStride : 4;
        return (firstElement * stride, elementCount * stride);
    }

    private static int ElementCount(Buffer buffer)
    {
        var stride = buffer.Description.StructureByteStride > 0 ? buffer.Description.StructureByteStride : 4;
        return buffer.Description.SizeInBytes / stride;
    }

    // A mip count of 0 or -1 means "the rest", which the backend reads as 0.
    private static TextureViewDescription Flatten(Format format, TextureDimension dimension, int firstMip, int mipCount, int firstSlice,
                                                  int arraySize)
        => new()
               {
                   Format = format,
                   Dimension = dimension,
                   FirstMip = Math.Max(0, firstMip),
                   MipCount = mipCount < 0 ? 0 : mipCount,
                   FirstArraySlice = Math.Max(0, firstSlice),
                   ArraySize = Math.Max(1, arraySize),
               };
}
