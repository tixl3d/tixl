#nullable enable
using System.IO;
using Newtonsoft.Json;
using T3.Core.Resource;
using T3.Serialization;

namespace T3.Editor.External;

/// <summary>
/// This is how we make HlslTools work with our shader linking
/// <seealso href="https://github.com/tgjones/HlslTools?tab=readme-ov-file#custom-preprocessor-definitions-and-additional-include-directories"/>
/// <br/>Todo: make generic for supporting other IDEs (jetbrains, vs, vim??)
/// </summary>
internal static class ShaderLinter
{
    public static void AddPackage(IResourcePackage package, IEnumerable<IResourcePackage>? additionalPackages, bool replaceExisting = false)
    {
        // package.ResourcesFolder now points to ".../Assets"
        var filePath = Path.Combine(package.AssetsFolder, FileName);
        var jsonObject = new HlslToolsJson(filePath);
        var resourceFolderList = jsonObject.IncludeDirectories;
        var virtualIncludeDirectories = jsonObject.VirtualDirectoryMappings;

        resourceFolderList.Add(package.AssetsFolder);
        if (package.Name != null)
        {
            // Maps "/ProjectName" to ".../ProjectName/Assets"
            virtualIncludeDirectories.Add('/' + package.Name, package.AssetsFolder);
        }

        if (additionalPackages is not null)
        {
            foreach (var p in additionalPackages)
            {
                resourceFolderList.Add(p.AssetsFolder);
                if (p.Name != null)
                {
                    virtualIncludeDirectories.TryAdd('/' + p.Name, p.AssetsFolder);
                }
            }
        }

        if (!package.IsReadOnly)
        {
            if (!JsonUtils.TrySaveJson(jsonObject, filePath))
            {
                Log.Error($"{nameof(ShaderLinter)}: failed to save {FileName} to \"{filePath}\"");
                return;
            }
        }

        if (!replaceExisting)
        {
            _hlslToolsJsons.Add(package, jsonObject);
        }
        else
        {
            var existing = _hlslToolsJsons.SingleOrDefault(x => x.Key.AssetsFolder == package.AssetsFolder);
            if (existing.Key != null)
            {
                _hlslToolsJsons.Remove(existing.Key);
            }
        }
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (Exception e)
        {
            Log.Error($"{nameof(ShaderLinter)}: failed to delete {filePath}: {e.Message}");
        }
    }

    [Serializable]
    private class HlslToolsJson(string filePath)
    {
        [JsonProperty("root")]
        public readonly bool Root = true;

        [JsonProperty("hlsl.preprocessorDefinitions")]
        public readonly string[] PreProcessorDefinitions = Array.Empty<string>();

        [JsonProperty("hlsl.additionalIncludeDirectories")]
        public readonly List<string> IncludeDirectories = ["."];

        [JsonProperty("hlsl.virtualDirectoryMappings")]
        public readonly Dictionary<string, string> VirtualDirectoryMappings = new();

        [JsonIgnore]
        public string FilePath = filePath;
    }

    public static void RemovePackage(IResourcePackage resourcePackage)
    {
        if (!_hlslToolsJsons.TryGetValue(resourcePackage, out var json))
        {
            Log.Error($"{nameof(ShaderLinter)}: failed to remove {resourcePackage.AssetsFolder}");
            return;
        }

        var filePath = json.FilePath;

        TryDelete(filePath);
        _hlslToolsJsons.Remove(resourcePackage);

        if (Program.IsShuttingDown)
            return;

        var resourceFolder = resourcePackage.AssetsFolder;
        foreach (var dependent in _hlslToolsJsons.Values)
        {
            if (dependent.IncludeDirectories.Remove(resourceFolder))
                JsonUtils.TrySaveJson(json, dependent.FilePath);
        }
    }
    
    private static readonly Dictionary<IResourcePackage, HlslToolsJson> _hlslToolsJsons = new();
    private const string FileName = "shadertoolsconfig.json";
}