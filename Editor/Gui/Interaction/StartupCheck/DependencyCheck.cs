#nullable enable
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using T3.Core.Resource.ShaderCompiling;
using T3.Core.SystemUi;
using T3.Editor.App;

namespace T3.Editor.Gui.Interaction.StartupCheck;

/// <summary>
/// Checks for the system dependencies TiXL can't bundle and explains what's missing, with the install command
/// for the user's distribution, before the main window is created.
/// </summary>
internal static class DependencyCheck
{
    private enum PackageFamily
    {
        Other,
        Arch,
        Debian,
        Fedora,
        Nix,
    }

    private readonly record struct MissingDependency(string Description, string InstallHint, bool PreventsStart);

    /// <summary>Returns false if TiXL should quit: the user chose to, or a missing dependency prevents starting.</summary>
    public static bool Run()
    {
        var missing = new List<MissingDependency>();
        CheckDotNetSdk(missing);

        if (ProgramWindows.UseVulkanBackend)
        {
            CheckVulkanLoader(missing);
            CheckSlangCompiler(missing);
        }

        if (missing.Count == 0)
            return true;

        var message = new StringBuilder();
        var preventsStart = false;
        foreach (var dependency in missing)
        {
            Log.Warning($"Missing dependency: {dependency.Description} {dependency.InstallHint}");
            message.Append(dependency.Description).Append('\n').Append(dependency.InstallHint).Append("\n\n");
            preventsStart |= dependency.PreventsStart;
        }

        if (preventsStart)
        {
            message.Append("TiXL can't start without it.");
            BlockingWindow.Instance.ShowMessageBox(message.ToString(), "Missing Dependencies", "Quit");
            return false;
        }

        message.Append("TiXL can start, but the features that need it won't work.");
        var choice = BlockingWindow.Instance.ShowMessageBox(message.ToString(), "Missing Dependencies", "Continue", "Quit");
        return choice != "Quit";
    }

    /** User projects and package updates are compiled with the dotnet CLI, the same way <c>Compiler</c> calls it. */
    private static void CheckDotNetSdk(List<MissingDependency> missing)
    {
        string sdkList;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("dotnet", "--list-sdks")
                                                  {
                                                      RedirectStandardOutput = true,
                                                      UseShellExecute = false,
                                                      CreateNoWindow = true,
                                                  });
            sdkList = process?.StandardOutput.ReadToEnd() ?? string.Empty;
            process?.WaitForExit(SdkQueryTimeoutMs);
        }
        catch (Win32Exception)
        {
            sdkList = string.Empty;
        }

        // Each line reads "10.0.112 [/usr/share/dotnet/sdk]".
        foreach (var line in sdkList.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var majorEnd = line.IndexOf('.');
            if (majorEnd > 0 && int.TryParse(line.AsSpan(0, majorEnd), out var major) && major >= RequiredSdkMajorVersion)
                return;
        }

        missing.Add(new MissingDependency($"The .NET {RequiredSdkMajorVersion} SDK was not found.",
                                          InstallHint(arch: "sudo pacman -S dotnet-sdk",
                                                      debian: $"sudo apt install dotnet-sdk-{RequiredSdkMajorVersion}.0",
                                                      fedora: $"sudo dnf install dotnet-sdk-{RequiredSdkMajorVersion}.0",
                                                      nix: $"dotnetCorePackages.sdk_{RequiredSdkMajorVersion}_0",
                                                      other: $"https://dotnet.microsoft.com/download/dotnet/{RequiredSdkMajorVersion}.0"),
                                          PreventsStart: false));
    }

    private static void CheckVulkanLoader(List<MissingDependency> missing)
    {
        var loaderName = OperatingSystem.IsWindows() ? "vulkan-1.dll" : "libvulkan.so.1";
        if (NativeLibrary.TryLoad(loaderName, out var handle))
        {
            NativeLibrary.Free(handle);
            return;
        }

        missing.Add(new MissingDependency("The Vulkan loader was not found.",
                                          InstallHint(arch: "sudo pacman -S vulkan-icd-loader",
                                                      debian: "sudo apt install libvulkan1 mesa-vulkan-drivers",
                                                      fedora: "sudo dnf install vulkan-loader mesa-vulkan-drivers",
                                                      nix: "vulkan-loader, and hardware.graphics.enable = true",
                                                      other: "Install your GPU driver's Vulkan support."),
                                          PreventsStart: true));
    }

    private static void CheckSlangCompiler(List<MissingDependency> missing)
    {
        if (SlangShaderCompiler.FindCompiler() != null)
            return;

        missing.Add(new MissingDependency("The Slang shader compiler (slangc) was not found.",
                                          InstallHint(arch: "Install shader-slang-bin from the AUR.",
                                                      debian: SlangReleaseHint,
                                                      fedora: SlangReleaseHint,
                                                      nix: "shader-slang",
                                                      other: SlangReleaseHint),
                                          PreventsStart: false));
    }

    private static string InstallHint(string arch, string debian, string fedora, string nix, string other)
    {
        if (OperatingSystem.IsWindows())
            return $"Install: {other}";

        return DetectPackageFamily() switch
                   {
                       PackageFamily.Arch   => $"Install: {arch}",
                       PackageFamily.Debian => $"Install: {debian}",
                       PackageFamily.Fedora => $"Install: {fedora}",
                       PackageFamily.Nix    => $"Add to your Nix packages: {nix}",
                       _                    => $"Install: {other}",
                   };
    }

    /** Derivatives (Manjaro, Mint, Nobara, ...) name their parent in ID_LIKE. */
    private static PackageFamily DetectPackageFamily()
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines("/etc/os-release");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return PackageFamily.Other;
        }

        var ids = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.StartsWith("ID=", StringComparison.Ordinal) || line.StartsWith("ID_LIKE=", StringComparison.Ordinal))
                ids.Append(line.AsSpan(line.IndexOf('=') + 1).Trim('"')).Append(' ');
        }

        foreach (var id in ids.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (id)
            {
                case "arch":
                    return PackageFamily.Arch;
                case "debian" or "ubuntu":
                    return PackageFamily.Debian;
                case "fedora" or "rhel":
                    return PackageFamily.Fedora;
                case "nixos":
                    return PackageFamily.Nix;
            }
        }

        return PackageFamily.Other;
    }

    private const int RequiredSdkMajorVersion = 10;
    private const int SdkQueryTimeoutMs = 5000;

    private const string SlangReleaseHint =
        "Download it from https://github.com/shader-slang/slang/releases, then put slangc on PATH or point TIXL_SLANGC at it.";
}
