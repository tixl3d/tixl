#nullable enable
using System;

namespace T3.IoServices.Midi;

/// <summary>
/// A stable, user-chosen MIDI device name that projects store instead of the physical product name,
/// mapped to the device present on this machine. The table lives in <see cref="MidiDeviceAliases"/>, so the
/// mapping is machine-local while projects stay portable.
/// </summary>
public sealed class MidiDeviceAlias
{
    /// <summary>Marks a device name as an alias. Physical product names never start with it.</summary>
    public const string Prefix = "TiXL:";

    /// <summary>The full alias including <see cref="Prefix"/>, as stored in projects.</summary>
    public string Alias = string.Empty;

    /// <summary>The physical device on this machine; empty while not set.</summary>
    public string ProductName = string.Empty;

    public static bool IsAlias(string? deviceName)
    {
        return !string.IsNullOrEmpty(deviceName) && deviceName.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public static string WithPrefix(string shortName)
    {
        return IsAlias(shortName) ? shortName : Prefix + shortName;
    }

    public static string WithoutPrefix(string alias)
    {
        return IsAlias(alias) ? alias[Prefix.Length..] : alias;
    }
}
