using T3.Graphics;
using Vortice.Vulkan;

namespace T3.Graphics.Vulkan;

/// <summary>
/// Placeholders for bindings a shader declares but a draw leaves unset. D3D11 read zeros from an unbound
/// slot and TiXL's operators rely on it — a disposed texture, an optional input, a slot cleared by an
/// enclosing operator. Vulkan would read an undefined descriptor, so it gets these instead.
/// </summary>
internal sealed class VulkanNullResources : IDisposable
{
    internal VulkanNullResources(VulkanBackend backend)
    {
        _backend = backend;

        var description = new TextureDescription
                              {
                                  Dimension = TextureDimension.Texture2D,
                                  Width = 1,
                                  Height = 1,
                                  Depth = 1,
                                  ArraySize = 1,
                                  MipLevels = 1,
                                  Format = Format.R8G8B8A8_UNorm,
                                  Samples = new SampleDescription(1, 0),
                                  Usage = TextureUsage.Sampled | TextureUsage.Storage | TextureUsage.CopyDestination,
                                  Memory = MemoryKind.DeviceLocal,
                              };

        _sampledTexture = (VulkanTexture)backend.CreateTexture(description, ReadOnlySpan<byte>.Empty, "null texture");
        _storageTexture = (VulkanTexture)backend.CreateTexture(description, ReadOnlySpan<byte>.Empty, "null storage texture");

        var viewDescription = new TextureViewDescription
                                  {
                                      Format = Format.R8G8B8A8_UNorm,
                                      Dimension = TextureDimension.Texture2D,
                                      MipCount = 1,
                                      ArraySize = 1,
                                  };

        _sampledView = (VulkanTextureView)backend.CreateTextureView(_sampledTexture, viewDescription, "null texture view");
        _storageView = (VulkanTextureView)backend.CreateTextureView(_storageTexture, viewDescription, "null storage view");
        _sampler = (VulkanSampler)backend.CreateSampler(new SamplerDescription { MaxAnisotropy = 1, MaxLod = 1 }, "null sampler");

        _buffer = (VulkanBuffer)backend.CreateBuffer(new GpuBufferDescription
                                                         {
                                                             SizeInBytes = 256,
                                                             Usage = BufferUsage.Constant | BufferUsage.Storage,
                                                             Memory = MemoryKind.DeviceLocal,
                                                         },
                                                     ReadOnlySpan<byte>.Empty, "null buffer");

        // Both images have to be in the layout their descriptor claims before anything samples them, and
        // nothing ever writes to them afterwards.
        backend.SubmitOneShot(commandBuffer =>
                              {
                                  VulkanBarriers.TransitionImage(backend, commandBuffer, _sampledTexture, VkImageLayout.ShaderReadOnlyOptimal,
                                                                 VkPipelineStageFlags2.AllCommands, VkAccessFlags2.ShaderSampledRead);
                                  VulkanBarriers.TransitionImage(backend, commandBuffer, _storageTexture, VkImageLayout.General,
                                                                 VkPipelineStageFlags2.AllCommands, VkAccessFlags2.ShaderStorageRead);
                              });
    }

    internal VkImageView SampledView => _sampledView.View;
    internal VkImageView StorageView => _storageView.View;
    internal VkSampler Sampler => _sampler.Sampler;
    internal VkBuffer Buffer => _buffer.Buffer;

    public void Dispose()
    {
        _sampledView.Dispose();
        _storageView.Dispose();
        _sampledTexture.Dispose();
        _storageTexture.Dispose();
        _sampler.Dispose();
        _buffer.Dispose();
    }

    private readonly VulkanBackend _backend;
    private readonly VulkanTexture _sampledTexture;
    private readonly VulkanTexture _storageTexture;
    private readonly VulkanTextureView _sampledView;
    private readonly VulkanTextureView _storageView;
    private readonly VulkanSampler _sampler;
    private readonly VulkanBuffer _buffer;
}
