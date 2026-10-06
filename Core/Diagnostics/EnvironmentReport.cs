#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using T3.Core.Resource;

namespace T3.Core.Diagnostics;

/// <summary>
/// Describes the machine well enough to reproduce a crash, and no better.
/// </summary>
/// <remarks>
/// Attached to crash reports and written to the log once at startup. A Linux bug report is nearly useless
/// without the distribution, the session type and the graphics driver, and those are exactly what a tester
/// cannot be expected to produce on request. Nothing here identifies a person: no user or host name, no
/// paths, no serials - the nickname the user chose is attached separately, by the reporter.
/// </remarks>
public static class EnvironmentReport
{
    /// <summary>
    /// Set once the graphics backend exists. A field rather than a lookup because the report is also written
    /// while the device is still being created, and because Graphics sits below Core and cannot push to it.
    /// </summary>
    public static string? GraphicsDescription { get; set; }

    /// <summary>Key/value lines for a crash report's context block, or the startup log.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Collect()
    {
        var lines = new List<KeyValuePair<string, string>>();

        if (OperatingSystem.IsLinux())
        {
            // OSDescription repeats the distribution here, while the kernel - which is what a graphics
            // driver bug turns on - appears in neither.
            Add(lines, "Distribution", ReadOsRelease("PRETTY_NAME") ?? RuntimeInformation.OSDescription);
            Add(lines, "Kernel", Environment.OSVersion.Version.ToString());
            Add(lines, "SessionType", Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"));
            Add(lines, "Desktop", Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"));
        }
        else
        {
            Add(lines, "OS", RuntimeInformation.OSDescription);
        }

        Add(lines, "Architecture", RuntimeInformation.OSArchitecture.ToString());
        Add(lines, "Runtime", RuntimeInformation.FrameworkDescription);
        Add(lines, "Processors", Environment.ProcessorCount.ToString());
        Add(lines, "Graphics", GraphicsDescription);

        foreach (var (name, details) in ThirdPartyRuntimeInfo.GetAll())
        {
            Add(lines, name, details);
        }

        return lines;
    }

    private static void Add(List<KeyValuePair<string, string>> lines, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(new KeyValuePair<string, string>(key, value));
        }
    }

    /// <summary>Reads one field from /etc/os-release, the only portable way to name a Linux distribution.</summary>
    private static string? ReadOsRelease(string key)
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/os-release"))
            {
                if (line.StartsWith(key + "=", StringComparison.Ordinal))
                {
                    return line[(key.Length + 1)..].Trim().Trim('"');
                }
            }
        }
        catch (Exception)
        {
            // An unreadable or absent os-release just means one line less in the report.
        }

        return null;
    }
}
