using T3.Graphics;
using Vortice.Vulkan;

namespace T3.Graphics.Vulkan;

internal static class VulkanConvert
{
    /// <summary>
    /// DXGI formats to Vulkan's. Only the formats TiXL can actually create are listed; anything else comes
    /// back as Undefined and is reported rather than silently rendering wrong.
    /// </summary>
    internal static VkFormat ToVulkan(Format format)
    {
        return format switch
                   {
                       Format.R32G32B32A32_Float    => VkFormat.R32G32B32A32Sfloat,
                       Format.R32G32B32A32_UInt     => VkFormat.R32G32B32A32Uint,
                       Format.R32G32B32A32_SInt     => VkFormat.R32G32B32A32Sint,
                       Format.R32G32B32_Float       => VkFormat.R32G32B32Sfloat,
                       Format.R32G32B32_UInt        => VkFormat.R32G32B32Uint,
                       Format.R32G32B32_SInt        => VkFormat.R32G32B32Sint,
                       Format.R16G16B16A16_Float    => VkFormat.R16G16B16A16Sfloat,
                       Format.R16G16B16A16_UNorm    => VkFormat.R16G16B16A16Unorm,
                       Format.R16G16B16A16_SNorm    => VkFormat.R16G16B16A16Snorm,
                       Format.R16G16B16A16_UInt     => VkFormat.R16G16B16A16Uint,
                       Format.R16G16B16A16_SInt     => VkFormat.R16G16B16A16Sint,
                       Format.R32G32_Float          => VkFormat.R32G32Sfloat,
                       Format.R32G32_UInt           => VkFormat.R32G32Uint,
                       Format.R32G32_SInt           => VkFormat.R32G32Sint,
                       Format.R8G8B8A8_UNorm        => VkFormat.R8G8B8A8Unorm,
                       Format.R8G8B8A8_UNorm_SRgb   => VkFormat.R8G8B8A8Srgb,
                       Format.R8G8B8A8_UInt         => VkFormat.R8G8B8A8Uint,
                       Format.R8G8B8A8_SNorm        => VkFormat.R8G8B8A8Snorm,
                       Format.R8G8B8A8_SInt         => VkFormat.R8G8B8A8Sint,
                       Format.B8G8R8A8_UNorm        => VkFormat.B8G8R8A8Unorm,
                       Format.B8G8R8A8_UNorm_SRgb   => VkFormat.B8G8R8A8Srgb,
                       Format.R16G16_Float          => VkFormat.R16G16Sfloat,
                       Format.R16G16_UNorm          => VkFormat.R16G16Unorm,
                       Format.R16G16_SNorm          => VkFormat.R16G16Snorm,
                       Format.R16G16_UInt           => VkFormat.R16G16Uint,
                       Format.R16G16_SInt           => VkFormat.R16G16Sint,
                       Format.R32_Float             => VkFormat.R32Sfloat,
                       Format.R32_UInt              => VkFormat.R32Uint,
                       Format.R32_SInt              => VkFormat.R32Sint,
                       Format.D32_Float             => VkFormat.D32Sfloat,
                       Format.D32_Float_S8X24_UInt  => VkFormat.D32SfloatS8Uint,
                       Format.D24_UNorm_S8_UInt     => VkFormat.D24UnormS8Uint,
                       Format.D16_UNorm             => VkFormat.D16Unorm,
                       Format.R10G10B10A2_UNorm     => VkFormat.A2B10G10R10UnormPack32,
                       Format.R10G10B10A2_UInt      => VkFormat.A2B10G10R10UintPack32,
                       Format.R11G11B10_Float       => VkFormat.B10G11R11UfloatPack32,
                       Format.R9G9B9E5_Sharedexp    => VkFormat.E5B9G9R9UfloatPack32,
                       Format.R8G8_UNorm            => VkFormat.R8G8Unorm,
                       Format.R8G8_SNorm            => VkFormat.R8G8Snorm,
                       Format.R8G8_UInt             => VkFormat.R8G8Uint,
                       Format.R8G8_SInt             => VkFormat.R8G8Sint,
                       Format.R16_Float             => VkFormat.R16Sfloat,
                       Format.R16_UNorm             => VkFormat.R16Unorm,
                       Format.R16_SNorm             => VkFormat.R16Snorm,
                       Format.R16_UInt              => VkFormat.R16Uint,
                       Format.R16_SInt              => VkFormat.R16Sint,
                       Format.R8_UNorm or Format.A8_UNorm => VkFormat.R8Unorm,
                       Format.R8_SNorm              => VkFormat.R8Snorm,
                       Format.R8_UInt               => VkFormat.R8Uint,
                       Format.R8_SInt               => VkFormat.R8Sint,
                       Format.B5G6R5_UNorm          => VkFormat.R5G6B5UnormPack16,
                       Format.B5G5R5A1_UNorm        => VkFormat.A1R5G5B5UnormPack16,
                       Format.B4G4R4A4_UNorm        => VkFormat.A4R4G4B4UnormPack16,
                       Format.BC1_UNorm             => VkFormat.Bc1RgbaUnormBlock,
                       Format.BC1_UNorm_SRgb        => VkFormat.Bc1RgbaSrgbBlock,
                       Format.BC2_UNorm             => VkFormat.Bc2UnormBlock,
                       Format.BC2_UNorm_SRgb        => VkFormat.Bc2SrgbBlock,
                       Format.BC3_UNorm             => VkFormat.Bc3UnormBlock,
                       Format.BC3_UNorm_SRgb        => VkFormat.Bc3SrgbBlock,
                       Format.BC4_UNorm             => VkFormat.Bc4UnormBlock,
                       Format.BC4_SNorm             => VkFormat.Bc4SnormBlock,
                       Format.BC5_UNorm             => VkFormat.Bc5UnormBlock,
                       Format.BC5_SNorm             => VkFormat.Bc5SnormBlock,
                       Format.BC6H_Uf16             => VkFormat.Bc6hUfloatBlock,
                       Format.BC6H_Sf16             => VkFormat.Bc6hSfloatBlock,
                       Format.BC7_UNorm             => VkFormat.Bc7UnormBlock,
                       Format.BC7_UNorm_SRgb        => VkFormat.Bc7SrgbBlock,
                       Format.Unknown               => VkFormat.Undefined,
                       _                            => Unsupported(format),
                   };
    }

    private static VkFormat Unsupported(Format format)
    {
        GraphicsLog.WarnOnce($"{format} has no Vulkan equivalent and is treated as undefined.");
        return VkFormat.Undefined;
    }

    internal static bool IsDepth(Format format)
        => format is Format.D32_Float or Format.D32_Float_S8X24_UInt or Format.D24_UNorm_S8_UInt or Format.D16_UNorm
                     or Format.R32_Typeless or Format.R24G8_Typeless;

    internal static bool HasStencil(Format format) => format is Format.D32_Float_S8X24_UInt or Format.D24_UNorm_S8_UInt;

    internal static VkImageAspectFlags AspectOf(Format format)
    {
        if (!IsDepth(format))
            return VkImageAspectFlags.Color;

        return HasStencil(format) ? VkImageAspectFlags.Depth | VkImageAspectFlags.Stencil : VkImageAspectFlags.Depth;
    }

    internal static VkImageUsageFlags ToVulkan(TextureUsage usage)
    {
        var flags = VkImageUsageFlags.None;

        if ((usage & TextureUsage.Sampled) != 0)
            flags |= VkImageUsageFlags.Sampled;

        if ((usage & TextureUsage.RenderTarget) != 0)
            flags |= VkImageUsageFlags.ColorAttachment;

        if ((usage & TextureUsage.DepthStencil) != 0)
            flags |= VkImageUsageFlags.DepthStencilAttachment;

        if ((usage & TextureUsage.Storage) != 0)
            flags |= VkImageUsageFlags.Storage;

        if ((usage & TextureUsage.CopySource) != 0)
            flags |= VkImageUsageFlags.TransferSrc;

        if ((usage & TextureUsage.CopyDestination) != 0)
            flags |= VkImageUsageFlags.TransferDst;

        return flags;
    }

    internal static VkBufferUsageFlags ToVulkan(BufferUsage usage)
    {
        var flags = VkBufferUsageFlags.None;

        if ((usage & BufferUsage.Constant) != 0)
            flags |= VkBufferUsageFlags.UniformBuffer;

        if ((usage & BufferUsage.Vertex) != 0)
            flags |= VkBufferUsageFlags.VertexBuffer;

        if ((usage & BufferUsage.Index) != 0)
            flags |= VkBufferUsageFlags.IndexBuffer;

        // Both of D3D11's buffer shapes are storage buffers in Vulkan: a structured buffer reads through the
        // same descriptor type, only its element layout differs.
        if ((usage & (BufferUsage.Structured | BufferUsage.Storage)) != 0)
            flags |= VkBufferUsageFlags.StorageBuffer;

        if ((usage & BufferUsage.Indirect) != 0)
            flags |= VkBufferUsageFlags.IndirectBuffer;

        if ((usage & BufferUsage.CopySource) != 0)
            flags |= VkBufferUsageFlags.TransferSrc;

        if ((usage & BufferUsage.CopyDestination) != 0)
            flags |= VkBufferUsageFlags.TransferDst;

        return flags;
    }

    internal static VkPrimitiveTopology ToVulkan(Topology topology)
    {
        return topology switch
                   {
                       Topology.PointList     => VkPrimitiveTopology.PointList,
                       Topology.LineList      => VkPrimitiveTopology.LineList,
                       Topology.LineStrip     => VkPrimitiveTopology.LineStrip,
                       Topology.TriangleStrip => VkPrimitiveTopology.TriangleStrip,
                       Topology.TriangleFan   => VkPrimitiveTopology.TriangleFan,
                       Topology.PatchList     => VkPrimitiveTopology.PatchList,
                       _                      => VkPrimitiveTopology.TriangleList,
                   };
    }

    internal static VkCompareOp ToVulkan(CompareFunction function)
    {
        return function switch
                   {
                       CompareFunction.Never        => VkCompareOp.Never,
                       CompareFunction.Less         => VkCompareOp.Less,
                       CompareFunction.Equal        => VkCompareOp.Equal,
                       CompareFunction.LessEqual    => VkCompareOp.LessOrEqual,
                       CompareFunction.Greater      => VkCompareOp.Greater,
                       CompareFunction.NotEqual     => VkCompareOp.NotEqual,
                       CompareFunction.GreaterEqual => VkCompareOp.GreaterOrEqual,
                       _                            => VkCompareOp.Always,
                   };
    }

    internal static VkBlendFactor ToVulkan(BlendFactor factor)
    {
        return factor switch
                   {
                       BlendFactor.Zero                        => VkBlendFactor.Zero,
                       BlendFactor.SourceColor                 => VkBlendFactor.SrcColor,
                       BlendFactor.InverseSourceColor          => VkBlendFactor.OneMinusSrcColor,
                       BlendFactor.SourceAlpha                 => VkBlendFactor.SrcAlpha,
                       BlendFactor.InverseSourceAlpha          => VkBlendFactor.OneMinusSrcAlpha,
                       BlendFactor.DestinationAlpha            => VkBlendFactor.DstAlpha,
                       BlendFactor.InverseDestinationAlpha     => VkBlendFactor.OneMinusDstAlpha,
                       BlendFactor.DestinationColor            => VkBlendFactor.DstColor,
                       BlendFactor.InverseDestinationColor     => VkBlendFactor.OneMinusDstColor,
                       BlendFactor.SourceAlphaSaturate         => VkBlendFactor.SrcAlphaSaturate,
                       BlendFactor.BlendFactorConstant         => VkBlendFactor.ConstantColor,
                       BlendFactor.InverseBlendFactorConstant  => VkBlendFactor.OneMinusConstantColor,
                       BlendFactor.SecondarySourceColor        => VkBlendFactor.Src1Color,
                       BlendFactor.InverseSecondarySourceColor => VkBlendFactor.OneMinusSrc1Color,
                       BlendFactor.SecondarySourceAlpha        => VkBlendFactor.Src1Alpha,
                       BlendFactor.InverseSecondarySourceAlpha => VkBlendFactor.OneMinusSrc1Alpha,
                       _                                       => VkBlendFactor.One,
                   };
    }

    internal static VkBlendOp ToVulkan(BlendOp operation)
    {
        return operation switch
                   {
                       BlendOp.Subtract        => VkBlendOp.Subtract,
                       BlendOp.ReverseSubtract => VkBlendOp.ReverseSubtract,
                       BlendOp.Min             => VkBlendOp.Min,
                       BlendOp.Max             => VkBlendOp.Max,
                       _                       => VkBlendOp.Add,
                   };
    }

    internal static VkColorComponentFlags ToVulkan(ColorComponents components)
    {
        var flags = VkColorComponentFlags.None;

        if ((components & ColorComponents.Red) != 0)
            flags |= VkColorComponentFlags.R;

        if ((components & ColorComponents.Green) != 0)
            flags |= VkColorComponentFlags.G;

        if ((components & ColorComponents.Blue) != 0)
            flags |= VkColorComponentFlags.B;

        if ((components & ColorComponents.Alpha) != 0)
            flags |= VkColorComponentFlags.A;

        return flags;
    }

    internal static VkStencilOp ToVulkan(StencilOp operation)
    {
        return operation switch
                   {
                       StencilOp.Zero           => VkStencilOp.Zero,
                       StencilOp.Replace        => VkStencilOp.Replace,
                       StencilOp.IncrementClamp => VkStencilOp.IncrementAndClamp,
                       StencilOp.DecrementClamp => VkStencilOp.DecrementAndClamp,
                       StencilOp.Invert         => VkStencilOp.Invert,
                       StencilOp.IncrementWrap  => VkStencilOp.IncrementAndWrap,
                       StencilOp.DecrementWrap  => VkStencilOp.DecrementAndWrap,
                       _                        => VkStencilOp.Keep,
                   };
    }

    internal static VkSamplerAddressMode ToVulkan(AddressMode mode)
    {
        return mode switch
                   {
                       AddressMode.MirrorRepeat      => VkSamplerAddressMode.MirroredRepeat,
                       AddressMode.ClampToEdge       => VkSamplerAddressMode.ClampToEdge,
                       AddressMode.ClampToBorder     => VkSamplerAddressMode.ClampToBorder,
                       AddressMode.MirrorClampToEdge => VkSamplerAddressMode.MirrorClampToEdge,
                       _                             => VkSamplerAddressMode.Repeat,
                   };
    }

    internal static VkFilter ToVulkan(FilterMode filter) => filter == FilterMode.Linear ? VkFilter.Linear : VkFilter.Nearest;

    internal static VkDescriptorType ToVulkan(BindingKind kind)
    {
        return kind switch
                   {
                       BindingKind.ConstantBuffer  => VkDescriptorType.UniformBuffer,
                       BindingKind.SampledTexture  => VkDescriptorType.SampledImage,
                       BindingKind.StorageTexture  => VkDescriptorType.StorageImage,
                       BindingKind.StructuredBuffer or BindingKind.StorageBuffer => VkDescriptorType.StorageBuffer,
                       _                           => VkDescriptorType.Sampler,
                   };
    }

    internal static VkShaderStageFlags ToVulkan(ShaderStage stage)
    {
        return stage switch
                   {
                       ShaderStage.Vertex   => VkShaderStageFlags.Vertex,
                       ShaderStage.Pixel    => VkShaderStageFlags.Fragment,
                       ShaderStage.Geometry => VkShaderStageFlags.Geometry,
                       _                    => VkShaderStageFlags.Compute,
                   };
    }
}
