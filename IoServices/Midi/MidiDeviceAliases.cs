#nullable enable
using System.Collections.Generic;
using System.IO;
using T3.Core.Logging;
using T3.Serialization;

namespace T3.IoServices.Midi;

/// <summary>
/// The machine-local alias table, a plain JSON list in its own file so it can be copied between machines
/// and ships next to an exported Player. The editor loads it from the settings folder, the Player from its
/// own folder.
/// </summary>
public static class MidiDeviceAliases
{
    public const string FileName = "midiAliases.json";

    /// <summary>In user order: the first alias mapped to a device is the one teaching writes.</summary>
    public static List<MidiDeviceAlias> Entries { get; private set; } = new();

    public static string? FilePath { get; private set; }

    /// <summary>Loads the table from <paramref name="directory"/>. A missing file yields an empty table.</summary>
    public static void Load(string directory)
    {
        FilePath = Path.Combine(directory, FileName);
        Entries = new List<MidiDeviceAlias>();
        _preserveFileOnDisk = false;

        if (File.Exists(FilePath))
        {
            if (JsonUtils.TryLoadingJson<List<MidiDeviceAlias>>(FilePath, out var loaded))
            {
                Entries = loaded;
            }
            else
            {
                // Never overwrite a file we could not read; the user's mapping may still be in it
                _preserveFileOnDisk = true;
                Log.Warning($"Could not read {FilePath}; MIDI aliases are not saved this session.");
            }
        }

        MidiConnectionManager.ReloadAliases();
    }

    /// <summary>Writes the table and refreshes the resolver. Call after any change to <see cref="Entries"/>.</summary>
    public static void Save()
    {
        MidiConnectionManager.ReloadAliases();
        if (FilePath == null || _preserveFileOnDisk)
            return;

        if (!JsonUtils.TrySaveJson(Entries, FilePath))
            Log.Warning($"Failed to write {FilePath}");
    }

    /// <summary>Removes the entry for <paramref name="alias"/>; the caller saves.</summary>
    public static bool Remove(string alias)
    {
        for (var index = 0; index < Entries.Count; index++)
        {
            if (Entries[index].Alias != alias)
                continue;

            Entries.RemoveAt(index);
            return true;
        }

        return false;
    }

    public static bool TryFind(string alias, out MidiDeviceAlias? entry)
    {
        foreach (var e in Entries)
        {
            if (e.Alias != alias)
                continue;

            entry = e;
            return true;
        }

        entry = null;
        return false;
    }

    private static bool _preserveFileOnDisk;
}
