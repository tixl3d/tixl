#nullable enable

using System;
using System.IO;
using T3.Core.Logging;
using T3.Core.Settings;
using T3.Serialization;

namespace T3.Core.IO;

/// <summary>
/// Implements writing and reading configuration files
/// </summary>
public class Settings<T> where T  : class, new()
{
    public static readonly T Defaults = new();

    #pragma warning disable CA2211
    public  static T Config = new();
    #pragma warning restore CA2211

    protected Settings(string relativeFilePath, bool saveOnQuit)
    {
        if (saveOnQuit)
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        _filePath = Path.Combine(ConfigDirectory, relativeFilePath);

        if (JsonUtils.TryLoadingJson<T>(_filePath, out var loaded))
        {
            Config = loaded;
        }
        else
        {
            Config = new T();

            // An existing-but-unreadable file (locked by another instance, or corrupt) must not be
            // overwritten with defaults on quit — that would silently destroy the user's real config.
            // A missing file is fine to (re)create.
            if (File.Exists(_filePath))
            {
                _preserveFileOnDisk = true;
                Log.Warning($"Could not read {_filePath}; using defaults and leaving the file untouched this session.");
            }
        }

        _instance = this;
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        Save();
    }

    public static void Save()
    {
        if (_instance == null || SaveDisabled || _preserveFileOnDisk)
            return;

        JsonUtils.TrySaveJson(Config, _instance._filePath);
    }

    private static Settings<T>? _instance;
    private readonly string _filePath;
    private static string ConfigDirectory => FileLocations.SettingsDirectory;

    // ReSharper disable once StaticMemberInGenericType
    public static bool SaveDisabled { get; set; }

    // Latched when the config file existed but couldn't be read, so Save() won't clobber it with defaults.
    // ReSharper disable once StaticMemberInGenericType
    private static bool _preserveFileOnDisk;
}