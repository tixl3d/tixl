#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// The machine-specific side of the output pipeline: device bindings and (later) window
/// placement and sync. Lives next to the setups in the project's .meta/ folder but is
/// per-computer and meant to be gitignored — a touring show rewrites it on first bind at
/// each venue while Setup and project never learn about display numbering.
/// </summary>
public sealed class MachineConfig
{
    public const int CurrentVersion = 1;
    public const string FileName = "outputs.machine.json";

    public List<DeviceBinding> Bindings = [];

    public DeviceBinding? TryGetBinding(Guid outputId)
    {
        foreach (var binding in Bindings)
        {
            if (binding.OutputId == outputId)
                return binding;
        }

        return null;
    }

    /// <summary>Adds or replaces the binding for an output.</summary>
    public void Bind(DeviceBinding binding)
    {
        Bindings.RemoveAll(b => b.OutputId == binding.OutputId);
        Bindings.Add(binding);
    }

    public void Unbind(Guid outputId)
    {
        Bindings.RemoveAll(b => b.OutputId == outputId);
    }

    public string ToJsonString()
    {
        var sb = new StringBuilder();
        using (var stringWriter = new StringWriter(sb))
        using (var writer = new JsonTextWriter(stringWriter))
        {
            writer.Formatting = Formatting.Indented;
            writer.WriteStartObject();
            writer.WriteValue("Version", CurrentVersion);
            writer.WritePropertyName("Bindings");
            writer.WriteStartArray();
            foreach (var binding in Bindings)
                binding.WriteToJson(writer);

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return sb.ToString();
    }

    public static MachineConfig ReadFromJson(JToken token)
    {
        var version = token.ReadValueSafe("Version", 0);
        if (version > CurrentVersion)
            Log.Warning($"Machine output config has format v{version} > v{CurrentVersion} — loading what we can.");

        return new MachineConfig
                   {
                       Bindings = token.ReadListSafe("Bindings", DeviceBinding.ReadFromJson),
                   };
    }

    public bool TrySaveToFile(string filePath)
    {
        try
        {
            File.WriteAllText(filePath, ToJsonString());
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Can't save machine output config to {filePath}: {e.Message}");
            return false;
        }
    }

    public static bool TryLoadFromFile(string filePath, out MachineConfig config)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            config = ReadFromJson(JObject.Parse(json));
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Can't load machine output config from {filePath}: {e.Message}");
            config = new MachineConfig();
            return false;
        }
    }
}
