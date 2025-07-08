#nullable enable

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using T3.Core.Compilation;
using T3.Core.Logging;

namespace T3.Core.UserData;

/// <summary>
/// A currently meek attempt at centralizing user data in a folder outside of the application or project folders.
/// Settings, logs, backups, variations, etc should be stored in this folder.
/// Also refers to "SettingsFolderInApplicationDirectory", which is used for defaults
/// Todo: should variations be included in project folders? Probably not, but we should think about how to best preserve and update them.
/// </summary>
public static class UserData
{
    static UserData()
    {
        Directory.CreateDirectory(FileLocations.ReadOnlySettingsPath);
        Directory.CreateDirectory(FileLocations.SettingsDirectory);
    }

    public static bool TryLoadOrInitializeUserData<T>(string relativeFilePath, [NotNullWhen(true)] out T? result)
    {
        result = default;
        if (!TryLoadingOrWriteDefaults(relativeFilePath, out var jsonString)) 
            return false;
        
        try
        {
            result = JsonConvert.DeserializeObject<T>(jsonString);
            return result != null;
        }
        catch (Exception)
        {
            return false;
        }
    }


    /// <summary>
    /// Returns the content of the settings file if it exists. Otherwise, initialize the file with content of default.
    /// </summary>
    public static bool TryLoadingOrWriteDefaults(string relativeFilePath, out string fileText)
    {
        var filePath = GetFilePath(relativeFilePath, UserDataLocation.User);
        if (File.Exists(filePath))
        {
            try
            {
                fileText = File.ReadAllText(filePath);
                return true;
            }
            catch
            {
                Log.Info($"User data file {filePath} could not be loaded.");
            }
        }

        var defaultsFilePath = GetFilePath(relativeFilePath, UserDataLocation.Defaults);
        if (File.Exists(defaultsFilePath))
        {
            try
            {
                fileText = File.ReadAllText(defaultsFilePath);

                try
                {
                    var directory = Path.GetDirectoryName(filePath);
                    Directory.CreateDirectory(directory!);
                    File.WriteAllText(filePath, fileText);
                }
                catch (Exception e)
                {
                    Log.Error($"User data file {filePath} could not be saved. Error: {e}");
                }

                return true;
            }
            catch (Exception e)
            {
                Log.Info($"User data file {defaultsFilePath} could not be loaded. Error: {e}");
            }
        }

        fileText = string.Empty;
        return false;
    }

    private static string GetFilePath(string relativeFilePath, UserDataLocation location)
    {
        var filePath = Path.Combine(location == UserDataLocation.User 
                                        ? FileLocations.SettingsDirectory 
                                        : FileLocations.ReadOnlySettingsPath, relativeFilePath);
        return filePath;
    }

    public static bool TrySave(string relativeFilePath, string fileContent, UserDataLocation location = UserDataLocation.User)
    {
        var filePath = GetFilePath(relativeFilePath, location);
        var directory = Path.GetDirectoryName(filePath);
        Directory.CreateDirectory(directory!);
        try
        {
            File.WriteAllText(filePath, fileContent);
            return true;
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save file {filePath}: {e}");
            return false;
        }
    }

    private static bool CanLoad(string relativeFileName, UserDataLocation location, out string filePath)
    {
        filePath = GetFilePath(relativeFileName, location);
        return File.Exists(filePath);
    }

    public static bool TryLoad(string relativeFilePath, UserDataLocation location, out string fileContent, out string filePath)
    {
        var canLoad = CanLoad(relativeFilePath, location, out filePath);
        if (!canLoad)
        {
            fileContent = string.Empty;
            return false;
        }

        try
        {
            fileContent = File.ReadAllText(filePath);
            return true;
        } 
        catch
        {
            Log.Info($"User data file {filePath} could not be loaded.");
            fileContent = string.Empty;
            return false;
        }
    }

    public static bool CanLoad(string relativeFileName)
    {
        var filePath = Path.Combine(FileLocations.SettingsDirectory, relativeFileName);
        if (File.Exists(filePath))
        {
            return true;
        }

        var defaultsFilePath = Path.Combine(FileLocations.ReadOnlySettingsPath, relativeFileName);
        return File.Exists(defaultsFilePath);
    }

    public enum UserDataLocation
    {
        Defaults,
        User
    }
}