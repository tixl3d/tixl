#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Logging;
using T3.Core.Settings;
using T3.IoServices.Midi;

namespace T3.Player;

/// <summary>
/// Loads the alias table shipped next to the Player and applies <c>--midi-alias</c> pairs on top, so a show
/// can be remapped on site by editing the file or the launch script.
/// </summary>
internal static class MidiAliasOverride
{
    public static void Apply(IEnumerable<string>? commandLinePairs)
    {
        MidiDeviceAliases.Load(FileLocations.StartFolder);
        var entries = MidiDeviceAliases.Entries;

        if (commandLinePairs != null)
        {
            foreach (var pair in commandLinePairs)
            {
                ApplyPair(entries, pair);
            }

            MidiConnectionManager.ReloadAliases();
        }

        if (entries.Count == 0)
            return;

        Log.Info($"MIDI aliases ({MidiDeviceAliases.FileName}):");
        foreach (var entry in entries)
        {
            var target = string.IsNullOrEmpty(entry.ProductName) ? "(not set)" : entry.ProductName;
            Log.Info($"  {entry.Alias} -> {target}");
        }
    }

    /// <summary>Parses <c>Alias=Device Name</c>; the prefix on the alias is optional.</summary>
    private static void ApplyPair(List<MidiDeviceAlias> entries, string pair)
    {
        var separatorIndex = pair.IndexOf('=');
        if (separatorIndex <= 0)
        {
            Log.Warning($"Ignoring MIDI alias argument '{pair}': expected Alias=DeviceName");
            return;
        }

        var alias = MidiDeviceAlias.WithPrefix(pair[..separatorIndex].Trim());
        var productName = pair[(separatorIndex + 1)..].Trim();

        if (MidiDeviceAliases.TryFind(alias, out var entry))
        {
            entry!.ProductName = productName;
            return;
        }

        entries.Add(new MidiDeviceAlias { Alias = alias, ProductName = productName });
    }
}
