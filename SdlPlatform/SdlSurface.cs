using System.Runtime.InteropServices;
using SDL;
using T3.Core.Logging;
using static SDL.SDL3;

namespace T3.SdlPlatform;

/// <summary>
/// What a graphics backend needs from an SDL window: the native handle for D3D11, the surface and its instance
/// extensions for Vulkan.
/// </summary>
public static unsafe class SdlSurface
{
    /// <summary>
    /// The Vulkan instance extensions this platform's surface needs. Only SDL knows them, and they have to be
    /// enabled when the instance is created, which happens before any window hands out a surface.
    /// </summary>
    public static IReadOnlyList<string> GetVulkanInstanceExtensions()
    {
        uint count;
        var names = SDL_Vulkan_GetInstanceExtensions(&count);
        if (names == null)
        {
            Log.Warning($"SDL could not report the Vulkan instance extensions: {SDL_GetError()}");
            return [];
        }

        var extensions = new List<string>((int)count);
        for (var i = 0; i < count; i++)
        {
            var name = Marshal.PtrToStringUTF8((nint)names[i]);
            if (!string.IsNullOrEmpty(name))
                extensions.Add(name);
        }

        return extensions;
    }

    /// <summary>A surface for a window created with <c>SDL_WINDOW_VULKAN</c>; zero on failure.</summary>
    public static nint CreateVulkanSurface(SDL_Window* window, nint instance)
    {
        VkSurfaceKHR_T* surface = null;
        return SDL_Vulkan_CreateSurface(window, (VkInstance_T*)instance, null, &surface) ? (nint)surface : IntPtr.Zero;
    }

    /// <summary>The Win32 handle behind a window on Windows, zero elsewhere.</summary>
    public static nint GetWin32Handle(SDL_Window* window)
    {
        return OperatingSystem.IsWindows()
                   ? SDL_GetPointerProperty(SDL_GetWindowProperties(window), SDL_PROP_WINDOW_WIN32_HWND_POINTER, IntPtr.Zero)
                   : IntPtr.Zero;
    }
}
