#nullable enable

using System;
using System.IO;
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
        Config = JsonUtils.TryLoadingJson<T>(_filePath) ?? new T();
        _instance = this;
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        Save();
    }

    public static void Save()
    {
        if (_instance == null || SaveDisabled)
            return;
        
        JsonUtils.TrySaveJson(Config, _instance._filePath);
    }

    private static Settings<T>? _instance;
    private readonly string _filePath;
    private static string ConfigDirectory => UserData.FileLocations.SettingsDirectory;

    // ReSharper disable once StaticMemberInGenericType
    public static bool SaveDisabled { get; set; }
}