using T3.Graphics;
using Vortice.Vulkan;

namespace T3.Graphics.Vulkan;

/// <summary>
/// An image plus the memory behind it, and the layout it is currently in. Vulkan has no automatic hazard
/// tracking, so the command list reads this to decide which barrier a use needs — that is what reproduces
/// D3D11's "bind it and it works".
/// </summary>
internal sealed unsafe class VulkanTexture(VulkanBackend backend, VkImage image, VkDeviceMemory memory, TextureDescription description, string? label)
    : GpuTexture(description, label)
{
    internal readonly VkImage Image = image;
    internal readonly VkDeviceMemory Memory = memory;
    internal readonly VkFormat VulkanFormat = VulkanConvert.ToVulkan(description.Format);
    internal readonly VkImageAspectFlags Aspect = VulkanConvert.AspectOf(description.Format);

    /// <summary>
    /// Tracked for the whole image rather than per subresource. Mip-by-mip transitions only matter for mip
    /// generation, which walks the levels itself.
    /// </summary>
    internal VkImageLayout Layout = VkImageLayout.Undefined;

    internal VkPipelineStageFlags2 LastStage = VkPipelineStageFlags2.TopOfPipe;
    internal VkAccessFlags2 LastAccess = VkAccessFlags2.None;

    /// <summary>
    /// Set instead of <see cref="Image"/> for upload and readback textures. D3D11's staging textures are only
    /// ever copied to and mapped, never bound, so a buffer with a texture's description is all they need —
    /// and unlike a linear-tiled image, every driver supports it.
    /// </summary>
    internal VkBuffer StagingBuffer;

    internal void* Mapped;

    /// <summary>False for a swapchain's images: the swapchain owns them and destroying them would be a bug.</summary>
    internal bool OwnsImage = true;

    protected override void ReleaseWhenRetired()
    {
        var image = OwnsImage ? Image : VkImage.Null;
        var staging = StagingBuffer;
        var memory = Memory;
        backend.Retire(api =>
                       {
                           if (!image.IsNull)
                               api.vkDestroyImage(image);

                           if (!staging.IsNull)
                               api.vkDestroyBuffer(staging);

                           if (!memory.IsNull)
                               api.vkFreeMemory(memory);
                       });
    }
}

internal sealed class VulkanTextureView : GpuTextureView
{
    internal VulkanTextureView(VulkanBackend backend, VulkanTexture texture, VkImageView view, TextureViewDescription description, string? label)
        : base(texture, description, label)
    {
        _backend = backend;
        View = view;
        Texture = texture;
        ImGuiTextureId = backend.RegisterImGuiTexture(this);
    }

    internal readonly VkImageView View;
    internal new readonly VulkanTexture Texture;

    /// <summary>
    /// A number the ImGui renderer looks up, not a pointer. A view disposed between building the draw list
    /// and drawing it simply stops resolving, which is what D3D11 got away with by accident.
    /// </summary>
    public override ulong ImGuiTextureId { get; }

    protected override void ReleaseWhenRetired()
    {
        var view = View;
        _backend.UnregisterImGuiTexture(ImGuiTextureId);
        _backend.Retire(api => api.vkDestroyImageView(view));
    }

    private readonly VulkanBackend _backend;
}

internal sealed unsafe class VulkanBuffer(VulkanBackend backend, VkBuffer buffer, VkDeviceMemory memory, GpuBufferDescription description, string? label)
    : GpuBuffer(description, label)
{
    internal readonly VkBuffer Buffer = buffer;
    internal readonly VkDeviceMemory Memory = memory;

    /// <summary>Upload and readback memory stays mapped: Vulkan allows it, and remapping per frame is wasted work.</summary>
    internal void* Mapped;

    /// <summary>
    /// An upload buffer holds one copy per frame in flight and rotates between them, so writing this frame's
    /// constants cannot overwrite what a frame still on the GPU is reading. D3D11 calls this renaming.
    /// </summary>
    internal int SliceCount = 1;

    internal ulong SliceStride;
    internal int CurrentSlice;
    internal int SliceWrittenInFrame = -1;

    /// <summary>Where the currently written copy starts. Descriptor writes and copies use it, not zero.</summary>
    internal ulong CurrentOffset => (ulong)CurrentSlice * SliceStride;

    internal VkPipelineStageFlags2 LastStage = VkPipelineStageFlags2.TopOfPipe;
    internal VkAccessFlags2 LastAccess = VkAccessFlags2.None;

    protected override void ReleaseWhenRetired()
    {
        var buffer = Buffer;
        var memory = Memory;
        backend.Retire(api =>
                       {
                           if (!buffer.IsNull)
                               api.vkDestroyBuffer(buffer);

                           if (!memory.IsNull)
                               api.vkFreeMemory(memory);
                       });
    }
}

internal sealed class VulkanSampler(VulkanBackend backend, VkSampler sampler, SamplerDescription description, string? label)
    : GpuSampler(description, label)
{
    internal readonly VkSampler Sampler = sampler;

    protected override void ReleaseWhenRetired()
    {
        var sampler = Sampler;
        backend.Retire(api => api.vkDestroySampler(sampler));
    }
}

internal sealed class VulkanShader(VulkanBackend backend, VkShaderModule module, ShaderStage stage, ShaderBinding[] bindings, string? label)
    : GpuShader(stage, label)
{
    internal readonly VkShaderModule Module = module;

    /// <summary>What the shader declares. The pipeline layout is built from the union across its stages.</summary>
    internal readonly ShaderBinding[] Bindings = bindings;

    protected override void ReleaseWhenRetired()
    {
        var module = Module;
        backend.Retire(api => api.vkDestroyShaderModule(module));
    }
}

internal sealed class VulkanPipeline(VulkanBackend backend, VkPipeline pipeline, VkPipelineLayout layout,
                                     VkDescriptorSetLayout[] setLayouts, VkPipelineBindPoint bindPoint,
                                     IReadOnlyDictionary<(int Set, int Slot), VkDescriptorType> declared, string? label)
    : GpuPipeline(label)
{
    internal readonly VkPipeline Pipeline = pipeline;
    internal readonly VkPipelineLayout Layout = layout;
    internal readonly VkPipelineBindPoint BindPoint = bindPoint;

    /// <summary>
    /// The descriptor type each declared binding was given. A draw may leave a binding unset — operators do
    /// that constantly — and the write still has to name the type the layout was created with.
    /// </summary>
    internal readonly IReadOnlyDictionary<(int Set, int Slot), VkDescriptorType> Declared = declared;

    protected override void ReleaseWhenRetired()
    {
        var pipeline = Pipeline;
        var layout = Layout;
        var layouts = setLayouts;
        backend.Retire(api =>
                       {
                           api.vkDestroyPipeline(pipeline);
                           api.vkDestroyPipelineLayout(layout);

                           foreach (var setLayout in layouts)
                           {
                               api.vkDestroyDescriptorSetLayout(setLayout);
                           }
                       });
    }
}
