#nullable enable
using System.IO;
using System.Reflection;

namespace T3.Editor.App.Startup;

/// <summary>
/// The command pieces needed to relaunch this editor: the host executable (the TiXL apphost,
/// or <c>dotnet</c> when launched as <c>dotnet TiXL.dll</c>) plus the managed dll in that case.
/// OS autostart entries and the crash watchdog embed these, so both keep working regardless of
/// how this instance was started.
/// </summary>
internal static class LaunchCommands
{
    /// <summary>Full path of the host executable; null if the runtime cannot provide it.</summary>
    internal static string? HostPath => Environment.ProcessPath;

    private static readonly string? _dllPath = DetectDllPath();

    /// <summary>Path of TiXL.dll when running under a dotnet host; null for apphost launches.</summary>
    internal static string? DllPath => _dllPath;

    private static string? DetectDllPath()
    {
        var hostName = Path.GetFileName(Environment.ProcessPath ?? string.Empty);
        var isDotnetHost = hostName.Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                           || hostName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);
        if (!isDotnetHost)
            return null;

        // The entry assembly's location is the built TiXL.dll — reliable even when argv doesn't
        // name it (e.g. `dotnet run`).
        var dll = Assembly.GetEntryAssembly()?.Location;
        return !string.IsNullOrEmpty(dll) && File.Exists(dll) ? dll : null;
    }

    /// <summary>The argument list (excluding the host) that relaunches the editor, ending with the given extra args.</summary>
    internal static List<string> BuildArgumentList(params string[] extraArgs)
    {
        var list = new List<string>();
        if (DllPath != null)
            list.Add(DllPath);

        list.AddRange(extraArgs);
        return list;
    }

    /// <summary>Quoted command line for the Windows Run registry value, e.g. <c>"C:\...\TiXL.exe" --autostart</c>.</summary>
    internal static string ToCommandLine(params string[] extraArgs)
    {
        var parts = new List<string> { QuoteWindows(HostPath ?? "TiXL") };
        parts.AddRange(BuildArgumentList(extraArgs).Select(a => QuoteWindows(a)));
        return string.Join(" ", parts);
    }

    private static string QuoteWindows(string argument)
        => argument.Contains(' ') ? "\"" + argument + "\"" : argument;

    /// <summary>Escapes one argument for a Desktop Entry <c>Exec=</c> line (XDG spec quoting).</summary>
    internal static string EscapeDesktopArg(string argument)
    {
        if (argument.Length == 0)
            return "\"\"";

        var needsQuotes = argument.Contains(' ') || argument.Contains('\t');
        var escaped = argument.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return needsQuotes ? "\"" + escaped + "\"" : escaped;
    }

    /// <summary>Escapes a string for embedding in an XML text node (the macOS LaunchAgent plist).</summary>
    internal static string EscapeXml(string text)
        => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
