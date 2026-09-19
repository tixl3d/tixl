using System.Runtime.InteropServices;
using SDL;
using T3.Core.Logging;
using Vortice.Vulkan;
using static SDL.SDL3;
using static Vortice.Vulkan.Vulkan;

namespace T3.Spikes.VulkanPlayer;

/// <summary>
/// Vulkan instance, device and the single graphics+present queue. Enables the Khronos validation layer
/// when it is installed and counts every validation error it reports.
/// </summary>
internal sealed unsafe class VulkanDevice : IDisposable
{
    public VulkanDevice(SDL_Window* window, bool enableValidation)
    {
        vkInitialize().CheckResult();

        var instanceExtensions = new List<VkUtf8String>();
        uint sdlExtensionCount;
        var sdlExtensions = SDL_Vulkan_GetInstanceExtensions(&sdlExtensionCount);
        for (var index = 0; index < sdlExtensionCount; index++)
        {
            instanceExtensions.Add(new VkUtf8String(sdlExtensions[index]));
        }

        var instanceLayers = new List<VkUtf8String>();
        if (enableValidation)
        {
            if (IsInstanceLayerAvailable(ValidationLayerName))
            {
                instanceLayers.Add(ValidationLayerName);
                instanceExtensions.Add(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
            }
            else
            {
                Log.Warning("Vulkan validation layer is not installed (Arch: pacman -S vulkan-validation-layers). Running without validation.");
            }
        }

        VkUtf8ReadOnlyString applicationName = "TiXL Vulkan Player Spike"u8;
        VkUtf8ReadOnlyString engineName = "TiXL"u8;
        VkApplicationInfo appInfo = new()
                                        {
                                            pApplicationName = applicationName,
                                            applicationVersion = new VkVersion(0, 1, 0),
                                            pEngineName = engineName,
                                            engineVersion = new VkVersion(5, 0, 0),
                                            apiVersion = VkVersion.Version_1_3,
                                        };

        using var layerNames = new VkStringArray(instanceLayers);
        using var extensionNames = new VkStringArray(instanceExtensions);

        VkInstanceCreateInfo instanceCreateInfo = new()
                                                      {
                                                          pApplicationInfo = &appInfo,
                                                          enabledLayerCount = layerNames.Length,
                                                          ppEnabledLayerNames = layerNames,
                                                          enabledExtensionCount = extensionNames.Length,
                                                          ppEnabledExtensionNames = extensionNames,
                                                      };

        VkDebugUtilsMessengerCreateInfoEXT debugCreateInfo = new()
                                                                 {
                                                                     messageSeverity = VkDebugUtilsMessageSeverityFlagsEXT.Error
                                                                                       | VkDebugUtilsMessageSeverityFlagsEXT.Warning,
                                                                     messageType = VkDebugUtilsMessageTypeFlagsEXT.General
                                                                                   | VkDebugUtilsMessageTypeFlagsEXT.Validation
                                                                                   | VkDebugUtilsMessageTypeFlagsEXT.Performance,
                                                                     pfnUserCallback = &OnDebugMessage,
                                                                 };
        var validationEnabled = instanceLayers.Count > 0;
        if (validationEnabled)
            instanceCreateInfo.pNext = &debugCreateInfo; // also reports errors from instance creation itself

        vkCreateInstance(&instanceCreateInfo, out Instance).CheckResult();
        InstanceApi = GetApi(Instance);

        if (validationEnabled)
        {
            InstanceApi.vkCreateDebugUtilsMessengerEXT(&debugCreateInfo, null, out _debugMessenger).CheckResult();
            Log.Info("Vulkan validation enabled");
        }

        VkSurfaceKHR_T* surfaceHandle = null;
        if (!SDL_Vulkan_CreateSurface(window, (VkInstance_T*)Instance.Handle, null, &surfaceHandle))
            throw new InvalidOperationException($"SDL_Vulkan_CreateSurface failed: {SDL_GetError()}");

        Surface = new VkSurfaceKHR((ulong)surfaceHandle);

        PhysicalDevice = PickPhysicalDevice(out QueueFamilyIndex);
        LogDeviceInfo();

        var priority = 1.0f;
        VkDeviceQueueCreateInfo queueCreateInfo = new()
                                                      {
                                                          queueFamilyIndex = QueueFamilyIndex,
                                                          queueCount = 1,
                                                          pQueuePriorities = &priority,
                                                      };

        List<VkUtf8String> deviceExtensions =
            [
                VK_KHR_SWAPCHAIN_EXTENSION_NAME,
                VK_KHR_PUSH_DESCRIPTOR_EXTENSION_NAME,
            ];
        using var deviceExtensionNames = new VkStringArray(deviceExtensions);

        VkPhysicalDeviceVulkan13Features vulkan13Features = new()
                                                                {
                                                                    dynamicRendering = true,
                                                                    synchronization2 = true,
                                                                };
        // Slang implements SV_VertexID as gl_VertexIndex - gl_BaseVertex to match D3D's draw-relative ids.
        VkPhysicalDeviceVulkan11Features vulkan11Features = new()
                                                                {
                                                                    pNext = &vulkan13Features,
                                                                    shaderDrawParameters = true,
                                                                };
        VkPhysicalDeviceFeatures2 features2 = new() { pNext = &vulkan11Features };

        VkDeviceCreateInfo deviceCreateInfo = new()
                                                  {
                                                      pNext = &features2,
                                                      queueCreateInfoCount = 1,
                                                      pQueueCreateInfos = &queueCreateInfo,
                                                      enabledExtensionCount = deviceExtensionNames.Length,
                                                      ppEnabledExtensionNames = deviceExtensionNames,
                                                  };

        InstanceApi.vkCreateDevice(PhysicalDevice, &deviceCreateInfo, null, out Device).CheckResult();
        DeviceApi = GetApi(Instance, Device);
        DeviceApi.vkGetDeviceQueue(QueueFamilyIndex, 0, out Queue);
    }

    public readonly VkInstance Instance;
    public readonly VkInstanceApi InstanceApi;
    public readonly VkSurfaceKHR Surface;
    public readonly VkPhysicalDevice PhysicalDevice;
    public readonly uint QueueFamilyIndex;
    public readonly VkDevice Device;
    public readonly VkDeviceApi DeviceApi;
    public readonly VkQueue Queue;

    public static int ValidationErrorCount => _validationErrorCount;

    public uint FindMemoryType(uint typeBits, VkMemoryPropertyFlags requiredProperties)
    {
        InstanceApi.vkGetPhysicalDeviceMemoryProperties(PhysicalDevice, out var memoryProperties);
        for (var typeIndex = 0; typeIndex < memoryProperties.memoryTypeCount; typeIndex++)
        {
            var isAllowed = (typeBits & (1u << typeIndex)) != 0;
            if (isAllowed && (memoryProperties.memoryTypes[typeIndex].propertyFlags & requiredProperties) == requiredProperties)
                return (uint)typeIndex;
        }

        throw new InvalidOperationException($"No Vulkan memory type with {requiredProperties}");
    }

    /// <summary>Records commands into a temporary command buffer and blocks until the GPU has executed them.</summary>
    public void SubmitAndWait(Action<VkCommandBuffer> record)
    {
        VkCommandPoolCreateInfo poolInfo = new()
                                               {
                                                   flags = VkCommandPoolCreateFlags.Transient,
                                                   queueFamilyIndex = QueueFamilyIndex,
                                               };
        DeviceApi.vkCreateCommandPool(&poolInfo, null, out var pool).CheckResult();
        DeviceApi.vkAllocateCommandBuffer(pool, out var commandBuffer).CheckResult();

        VkCommandBufferBeginInfo beginInfo = new() { flags = VkCommandBufferUsageFlags.OneTimeSubmit };
        DeviceApi.vkBeginCommandBuffer(commandBuffer, &beginInfo).CheckResult();
        record(commandBuffer);
        DeviceApi.vkEndCommandBuffer(commandBuffer).CheckResult();

        VkSubmitInfo submitInfo = new()
                                      {
                                          commandBufferCount = 1,
                                          pCommandBuffers = &commandBuffer,
                                      };
        DeviceApi.vkQueueSubmit(Queue, 1, &submitInfo, VkFence.Null).CheckResult();
        DeviceApi.vkQueueWaitIdle(Queue).CheckResult();
        DeviceApi.vkDestroyCommandPool(pool);
    }

    public void TransitionImage(VkCommandBuffer commandBuffer, VkImage image,
                                VkImageLayout oldLayout, VkImageLayout newLayout,
                                VkPipelineStageFlags2 sourceStage, VkAccessFlags2 sourceAccess,
                                VkPipelineStageFlags2 destinationStage, VkAccessFlags2 destinationAccess)
    {
        VkImageMemoryBarrier2 barrier = new()
                                            {
                                                srcStageMask = sourceStage,
                                                srcAccessMask = sourceAccess,
                                                dstStageMask = destinationStage,
                                                dstAccessMask = destinationAccess,
                                                oldLayout = oldLayout,
                                                newLayout = newLayout,
                                                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                                                dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
                                                image = image,
                                                subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
                                            };
        VkDependencyInfo dependencyInfo = new()
                                              {
                                                  imageMemoryBarrierCount = 1,
                                                  pImageMemoryBarriers = &barrier,
                                              };
        DeviceApi.vkCmdPipelineBarrier2(commandBuffer, &dependencyInfo);
    }

    public void Dispose()
    {
        DeviceApi.vkDeviceWaitIdle();
        DeviceApi.vkDestroyDevice();
        InstanceApi.vkDestroySurfaceKHR(Surface);
        if (_debugMessenger != VkDebugUtilsMessengerEXT.Null)
            InstanceApi.vkDestroyDebugUtilsMessengerEXT(_debugMessenger);

        InstanceApi.vkDestroyInstance();
    }

    private VkPhysicalDevice PickPhysicalDevice(out uint queueFamilyIndex)
    {
        uint deviceCount = 0;
        InstanceApi.vkEnumeratePhysicalDevices(&deviceCount, null).CheckResult();
        if (deviceCount == 0)
            throw new InvalidOperationException("No GPU with Vulkan support found");

        var devices = new VkPhysicalDevice[deviceCount];
        InstanceApi.vkEnumeratePhysicalDevices(devices).CheckResult();

        var bestDevice = VkPhysicalDevice.Null;
        queueFamilyIndex = 0;
        foreach (var candidate in devices)
        {
            if (!SupportsRequiredFeatures(candidate) || !TryFindGraphicsPresentQueue(candidate, out var candidateQueueFamily))
                continue;

            InstanceApi.vkGetPhysicalDeviceProperties(candidate, out var properties);
            var isDiscrete = properties.deviceType == VkPhysicalDeviceType.DiscreteGpu;
            if (bestDevice.IsNull || isDiscrete)
            {
                bestDevice = candidate;
                queueFamilyIndex = candidateQueueFamily;
                if (isDiscrete)
                    break;
            }
        }

        if (bestDevice.IsNull)
            throw new InvalidOperationException("No GPU supports Vulkan 1.3 dynamic rendering, synchronization2, shader draw parameters and push descriptors");

        return bestDevice;
    }

    private bool SupportsRequiredFeatures(VkPhysicalDevice physicalDevice)
    {
        InstanceApi.vkGetPhysicalDeviceProperties(physicalDevice, out var properties);
        if (properties.apiVersion < VkVersion.Version_1_3)
            return false;

        VkPhysicalDeviceVulkan13Features vulkan13Features = new();
        VkPhysicalDeviceVulkan11Features vulkan11Features = new() { pNext = &vulkan13Features };
        VkPhysicalDeviceFeatures2 features2 = new() { pNext = &vulkan11Features };
        InstanceApi.vkGetPhysicalDeviceFeatures2(physicalDevice, &features2);
        if (!vulkan13Features.dynamicRendering || !vulkan13Features.synchronization2 || !vulkan11Features.shaderDrawParameters)
            return false;

        uint extensionCount = 0;
        InstanceApi.vkEnumerateDeviceExtensionProperties(physicalDevice, null, &extensionCount, null).CheckResult();
        var extensions = new VkExtensionProperties[extensionCount];
        fixed (VkExtensionProperties* extensionsPtr = extensions)
        {
            InstanceApi.vkEnumerateDeviceExtensionProperties(physicalDevice, null, &extensionCount, extensionsPtr).CheckResult();
        }

        var hasSwapchain = false;
        var hasPushDescriptor = false;
        for (var index = 0; index < extensions.Length; index++)
        {
            fixed (byte* namePtr = extensions[index].extensionName)
            {
                var name = new VkUtf8String(namePtr);
                hasSwapchain |= name == VK_KHR_SWAPCHAIN_EXTENSION_NAME;
                hasPushDescriptor |= name == VK_KHR_PUSH_DESCRIPTOR_EXTENSION_NAME;
            }
        }

        return hasSwapchain && hasPushDescriptor;
    }

    private bool TryFindGraphicsPresentQueue(VkPhysicalDevice physicalDevice, out uint familyIndex)
    {
        uint familyCount = 0;
        InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, null);
        var families = new VkQueueFamilyProperties[familyCount];
        fixed (VkQueueFamilyProperties* familiesPtr = families)
        {
            InstanceApi.vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &familyCount, familiesPtr);
        }

        for (familyIndex = 0; familyIndex < familyCount; familyIndex++)
        {
            if ((families[familyIndex].queueFlags & VkQueueFlags.Graphics) == 0)
                continue;

            InstanceApi.vkGetPhysicalDeviceSurfaceSupportKHR(physicalDevice, familyIndex, Surface, out var canPresent);
            if (canPresent)
                return true;
        }

        return false;
    }

    private void LogDeviceInfo()
    {
        VkPhysicalDeviceVulkan12Properties vulkan12Properties = new();
        VkPhysicalDeviceProperties2 properties2 = new() { pNext = &vulkan12Properties };
        InstanceApi.vkGetPhysicalDeviceProperties2(PhysicalDevice, &properties2);

        var deviceName = new VkUtf8String(properties2.properties.deviceName).ToString();
        var driverName = new VkUtf8String(vulkan12Properties.driverName).ToString();
        var driverInfo = new VkUtf8String(vulkan12Properties.driverInfo).ToString();
        var apiVersion = properties2.properties.apiVersion;
        Log.Info($"GPU: {deviceName} ({properties2.properties.deviceType}), driver {driverName} {driverInfo}, Vulkan {apiVersion.Major}.{apiVersion.Minor}.{apiVersion.Patch}");
    }

    private static bool IsInstanceLayerAvailable(VkUtf8String layerName)
    {
        uint layerCount = 0;
        vkEnumerateInstanceLayerProperties(&layerCount, null).CheckResult();
        var layers = new VkLayerProperties[layerCount];
        vkEnumerateInstanceLayerProperties(layers).CheckResult();
        for (var index = 0; index < layers.Length; index++)
        {
            fixed (byte* namePtr = layers[index].layerName)
            {
                if (new VkUtf8String(namePtr) == layerName)
                    return true;
            }
        }

        return false;
    }

    [UnmanagedCallersOnly]
    private static uint OnDebugMessage(VkDebugUtilsMessageSeverityFlagsEXT severity,
                                       VkDebugUtilsMessageTypeFlagsEXT types,
                                       VkDebugUtilsMessengerCallbackDataEXT* callbackData,
                                       void* userData)
    {
        var message = new VkUtf8String(callbackData->pMessage).ToString();
        if ((severity & VkDebugUtilsMessageSeverityFlagsEXT.Error) != 0)
        {
            Interlocked.Increment(ref _validationErrorCount);
            Log.Error($"[Vulkan {types}] {message}");
        }
        else
        {
            Log.Warning($"[Vulkan {types}] {message}");
        }

        return VK_FALSE;
    }

    private static readonly VkUtf8String ValidationLayerName = "VK_LAYER_KHRONOS_validation"u8;
    private static int _validationErrorCount;
    private readonly VkDebugUtilsMessengerEXT _debugMessenger = VkDebugUtilsMessengerEXT.Null;
}
