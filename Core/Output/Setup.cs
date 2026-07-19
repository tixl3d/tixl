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
/// Everything that is re-done when the physical situation changes: surfaces, reference
/// images, outputs, props. One JSON file per venue, stored in the project's .meta/ folder.
/// Ops bind to the contained entities by GUID; duplicating a Setup preserves those GUIDs —
/// that is the venue-swap mechanism.
/// </summary>
public sealed class Setup
{
    public const int CurrentVersion = 1;

    /// <summary>Setups live in the project's meta folder as &lt;name&gt;.setup.json.</summary>
    public const string FileSuffix = ".setup.json";
    public const string FolderName = ".meta";

    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public List<ReferenceImage> ReferenceImages = [];
    public List<Surface> Surfaces = [];
    public List<OutputDefinition> Outputs = [];
    public List<Prop> Props = [];

    /// <summary>Creates a setup containing the always-present Default output.</summary>
    public static Setup CreateDefault(string name = "Setup 1")
    {
        return new Setup
                   {
                       Name = name,
                       Outputs = [new OutputDefinition { Name = "Default", Kind = OutputDefinition.Kinds.Default }],
                   };
    }

    /// <summary>
    /// Deep copy with a new setup id but IDENTICAL entity GUIDs — op bindings stay intact
    /// across venues (duplicate → re-trace → re-calibrate).
    /// </summary>
    public Setup Duplicate(string newName)
    {
        var json = ToJsonString();
        var clone = ReadFromJson(JObject.Parse(json)) ?? throw new InvalidOperationException("Setup round-trip failed during duplication");
        clone.Id = Guid.NewGuid();
        clone.Name = newName;
        return clone;
    }

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteValue("Version", CurrentVersion);
        writer.WriteObject("Id", Id);
        writer.WriteString("Name", Name);

        writer.WritePropertyName("ReferenceImages");
        writer.WriteStartArray();
        foreach (var image in ReferenceImages)
            image.WriteToJson(writer);

        writer.WriteEndArray();

        writer.WritePropertyName("Surfaces");
        writer.WriteStartArray();
        foreach (var surface in Surfaces)
            surface.WriteToJson(writer);

        writer.WriteEndArray();

        writer.WritePropertyName("Outputs");
        writer.WriteStartArray();
        foreach (var output in Outputs)
            output.WriteToJson(writer);

        writer.WriteEndArray();

        writer.WritePropertyName("Props");
        writer.WriteStartArray();
        foreach (var prop in Props)
            prop.WriteToJson(writer);

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public static Setup? ReadFromJson(JToken token)
    {
        var version = token.ReadValueSafe("Version", 0);
        if (version > CurrentVersion)
            Log.Warning($"Setup '{token.ReadValueSafe("Name", "?")}' was written by a newer TiXL (format v{version} > v{CurrentVersion}) — loading what we can.");

        return new Setup
                   {
                       Id = OutputJson.ReadGuid(token["Id"]),
                       Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                       ReferenceImages = token.ReadListSafe("ReferenceImages", ReferenceImage.ReadFromJson),
                       Surfaces = token.ReadListSafe("Surfaces", Surface.ReadFromJson),
                       Outputs = token.ReadListSafe("Outputs", OutputDefinition.ReadFromJson),
                       Props = token.ReadListSafe("Props", Prop.ReadFromJson),
                   };
    }

    public string ToJsonString()
    {
        var sb = new StringBuilder();
        using (var stringWriter = new StringWriter(sb))
        using (var writer = new JsonTextWriter(stringWriter))
        {
            writer.Formatting = Formatting.Indented;
            WriteToJson(writer);
            writer.Flush();
        }

        return sb.ToString();
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
            Log.Warning($"Can't save setup to {filePath}: {e.Message}");
            return false;
        }
    }

    public static bool TryLoadFromFile(string filePath, out Setup? setup)
    {
        setup = null;
        try
        {
            var json = File.ReadAllText(filePath);
            setup = ReadFromJson(JObject.Parse(json));
            return setup != null;
        }
        catch (Exception e)
        {
            Log.Warning($"Can't load setup from {filePath}: {e.Message}");
            return false;
        }
    }
}
