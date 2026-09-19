using Vortice.Vulkan;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// Sampler objects as [SamplerState] describes them: a filter and an address mode per axis. The real backend
/// caches these by description, because operators recreate their state objects whenever they are dirty.
/// </summary>
internal sealed unsafe class Samplers : IDisposable
{
    public Samplers(VulkanDevice device)
    {
        _device = device;
    }

    public VkSampler Get(VkFilter filter, VkSamplerAddressMode addressMode)
    {
        var key = ((int)filter, (int)addressMode);
        if (_samplers.TryGetValue(key, out var existing))
            return existing;

        VkSamplerCreateInfo samplerInfo = new()
                                              {
                                                  magFilter = filter,
                                                  minFilter = filter,
                                                  mipmapMode = filter == VkFilter.Linear
                                                                   ? VkSamplerMipmapMode.Linear
                                                                   : VkSamplerMipmapMode.Nearest,
                                                  addressModeU = addressMode,
                                                  addressModeV = addressMode,
                                                  addressModeW = addressMode,
                                                  maxLod = 1000,
                                              };
        _device.DeviceApi.vkCreateSampler(&samplerInfo, null, out var sampler).CheckResult();
        _samplers[key] = sampler;
        return sampler;
    }

    public void Dispose()
    {
        foreach (var sampler in _samplers.Values)
        {
            _device.DeviceApi.vkDestroySampler(sampler);
        }

        _samplers.Clear();
    }

    private readonly VulkanDevice _device;
    private readonly Dictionary<(int, int), VkSampler> _samplers = [];
}
