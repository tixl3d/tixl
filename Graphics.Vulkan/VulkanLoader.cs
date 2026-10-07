namespace T3.Graphics.Vulkan;

/// <summary>
/// Where the Vulkan loader is, on systems where asking for it by name does not find it.
/// </summary>
/// <remarks>
/// On macOS a bare library name only searches /usr/local/lib and /usr/lib, so a Homebrew loader in
/// /opt/homebrew/lib is invisible to both Vortice and SDL. Linux and Windows have the loader on the default
/// search path and need none of this.
/// </remarks>
public static class VulkanLoader
{
    /// <summary>
    /// The full path of the loader to use, or null to let the default name lookup find it. Both the backend
    /// and the window layer must use the same one: SDL creates the surface through the loader it loaded itself.
    /// </summary>
    public static string? FindLibraryPath()
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        const string fileName = "libvulkan.1.dylib";

        // Next to the executable first, so a bundled loader wins over whatever is installed.
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, fileName) };

        var sdk = Environment.GetEnvironmentVariable("VULKAN_SDK");
        if (!string.IsNullOrEmpty(sdk))
            candidates.Add(Path.Combine(sdk, "lib", fileName));

        candidates.Add("/usr/local/lib/" + fileName);
        candidates.Add("/opt/homebrew/lib/" + fileName);

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
