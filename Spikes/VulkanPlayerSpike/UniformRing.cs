using Vortice.Vulkan;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// Per-frame constant buffer storage: every write takes a fresh slice, so updating constants never waits for
/// the GPU. This is what D3D11's Map(WriteDiscard) does for TiXL's operators, which rewrite their constants
/// every frame.
/// </summary>
internal sealed unsafe class UniformRing : IDisposable
{
    public UniformRing(VulkanDevice device, int framesInFlight, int bytesPerFrame = 64 * 1024)
    {
        _device = device;
        _bytesPerFrame = bytesPerFrame;

        device.InstanceApi.vkGetPhysicalDeviceProperties(device.PhysicalDevice, out var properties);
        _offsetAlignment = (int)Math.Max(properties.limits.minUniformBufferOffsetAlignment, 4);

        var size = (ulong)(bytesPerFrame * framesInFlight);
        VkBufferCreateInfo bufferInfo = new() { size = size, usage = VkBufferUsageFlags.UniformBuffer };
        device.DeviceApi.vkCreateBuffer(&bufferInfo, null, out Buffer).CheckResult();

        device.DeviceApi.vkGetBufferMemoryRequirements(Buffer, out var requirements);
        VkMemoryAllocateInfo allocateInfo = new()
                                                {
                                                    allocationSize = requirements.size,
                                                    memoryTypeIndex = device.FindMemoryType(requirements.memoryTypeBits,
                                                                                            VkMemoryPropertyFlags.HostVisible
                                                                                            | VkMemoryPropertyFlags.HostCoherent),
                                                };
        device.DeviceApi.vkAllocateMemory(&allocateInfo, null, out _memory).CheckResult();
        device.DeviceApi.vkBindBufferMemory(Buffer, _memory, 0).CheckResult();

        void* mapped;
        device.DeviceApi.vkMapMemory(_memory, 0, size, 0, &mapped).CheckResult();
        _mapped = (byte*)mapped;
    }

    public readonly VkBuffer Buffer;

    public void BeginFrame(int frameIndex)
    {
        _frameStart = frameIndex * _bytesPerFrame;
        _used = 0;
    }

    /// <summary>Copies <paramref name="data"/> into this frame's slice and returns where it landed.</summary>
    public (VkBuffer Buffer, ulong Offset, ulong Range) Write<T>(in T data) where T : unmanaged
    {
        var size = sizeof(T);
        if (_used + size > _bytesPerFrame)
            throw new InvalidOperationException($"Uniform ring holds {_bytesPerFrame} bytes per frame");

        var offset = _frameStart + _used;
        fixed (T* source = &data)
        {
            System.Buffer.MemoryCopy(source, _mapped + offset, size, size);
        }

        _used += (size + _offsetAlignment - 1) / _offsetAlignment * _offsetAlignment;
        return (Buffer, (ulong)offset, (ulong)size);
    }

    public void Dispose()
    {
        _device.DeviceApi.vkUnmapMemory(_memory);
        _device.DeviceApi.vkDestroyBuffer(Buffer);
        _device.DeviceApi.vkFreeMemory(_memory);
    }

    private readonly VulkanDevice _device;
    private readonly VkDeviceMemory _memory;
    private readonly byte* _mapped;
    private readonly int _bytesPerFrame;
    private readonly int _offsetAlignment;
    private int _frameStart;
    private int _used;
}
