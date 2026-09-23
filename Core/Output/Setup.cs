#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes.Vector;
using T3.Core.Logging;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// Everything that is re-done when the physical situation changes: surfaces, reference
/// images, outputs, props. One JSON file per venue, stored in the project's .meta/Setups/ folder.
/// Ops bind to the contained entities by GUID; duplicating a Setup preserves those GUIDs —
/// that is the venue-swap mechanism.
/// </summary>
public sealed class Setup
{
    public const int CurrentVersion = 1;

    /// <summary>Setups live in <see cref="SetupFiles.FolderIn"/> as &lt;name&gt;.setup.json.</summary>
    public const string FileSuffix = ".setup.json";
    public const string FolderName = "Setups";

    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public List<ReferenceImage> ReferenceImages = [];
    public List<Surface> Surfaces = [];

    /// <summary>Pixel sources, one per supplying op, and the rectangles cut from them. Routing lives here
    /// rather than on the ops: a surface names the slice it shows, so it survives re-instantiation and
    /// duplicates with the setup.</summary>
    public List<ContentSource> ContentSources = [];

    public List<Slice> Slices = [];
    public List<OutputDefinition> Outputs = [];
    public List<Prop> Props = [];

    /// <summary>Venue footprints whose segments carry the wall surfaces standing on them.</summary>
    public List<FloorPlan> FloorPlans = [];

    /// <summary>Creates a setup containing the always-present Default output.</summary>
    public static Setup CreateDefault(string name = "Setup 1")
    {
        return new Setup
                   {
                       Name = name,
                       // A format, not a canvas anything is plugged into, so it carries a size rather than following one.
                       Outputs =
                       [
                           new OutputDefinition
                               {
                                   Name = "Default",
                                   Kind = OutputDefinition.Kinds.Default,
                                   CanvasResolution = new Int2(1920, 1080),
                               },
                       ],
                   };
    }

    /// <summary>
    /// Deep copy with a new setup id but IDENTICAL entity GUIDs — op bindings stay intact
    /// across venues (duplicate → re-trace → re-calibrate).
    /// </summary>
    public Setup Duplicate(string newName)
    {
        var json = ToJsonString();
        var clone = ReadFromJson(JObject.Parse(json));
        clone.Id = Guid.NewGuid();
        clone.Name = newName;
        return clone;
    }

    // Lookup helpers — plain loops, so per-frame callers don't pay a List.Find closure per call.
    public Surface? FindSurface(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        foreach (var surface in Surfaces)
        {
            if (surface.Id == id)
                return surface;
        }

        return null;
    }

    /// <summary>
    /// The surface that carries the corner pins for <paramref name="surfaceId"/>: itself when it has output
    /// mappings, else the nearest ancestor with mappings. A Layout child has no mapping of its own — it is
    /// shown wherever its parent is mapped. Null when nothing in the chain is mapped.
    /// </summary>
    public Surface? FindMappedAncestor(Guid surfaceId)
    {
        var surface = FindSurface(surfaceId);
        for (var guard = 0; surface != null && guard < 16; guard++)
        {
            if (surface.OutputMappings.Count > 0)
                return surface;

            if (surface.ParentId == Guid.Empty)
                break;

            var parentId = surface.ParentId;
            surface = FindSurface(parentId);
        }

        return null;
    }

    public OutputDefinition? FindOutput(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        foreach (var output in Outputs)
        {
            if (output.Id == id)
                return output;
        }

        return null;
    }

    /// <summary>A patch by id, with the output that owns it.</summary>
    public OutputDefinition.Patch? FindPatch(Guid id, out OutputDefinition? owner)
    {
        owner = null;
        if (id == Guid.Empty)
            return null;

        foreach (var output in Outputs)
        {
            foreach (var patch in output.Patches)
            {
                if (patch.Id != id)
                    continue;

                owner = output;
                return patch;
            }
        }

        return null;
    }

    public Slice? FindSlice(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        foreach (var slice in Slices)
        {
            if (slice.Id == id)
                return slice;
        }

        return null;
    }

    public ContentSource? FindSource(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        foreach (var source in ContentSources)
        {
            if (source.Id == id)
                return source;
        }

        return null;
    }

    /// <summary>Content items and suppliers address sources by the supplying op's SymbolChildId, not by entity id.</summary>
    public ContentSource? FindSourceByChildId(Guid symbolChildId)
    {
        if (symbolChildId == Guid.Empty)
            return null;

        foreach (var source in ContentSources)
        {
            if (source.SymbolChildId == symbolChildId)
                return source;
        }

        return null;
    }

    public ReferenceImage? FindReferenceImage(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        foreach (var image in ReferenceImages)
        {
            if (image.Id == id)
                return image;
        }

        return null;
    }

    public Prop? FindProp(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        foreach (var prop in Props)
        {
            if (prop.Id == id)
                return prop;
        }

        return null;
    }

    public FloorPlan? FindFloorPlan(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        foreach (var plan in FloorPlans)
        {
            if (plan.Id == id)
                return plan;
        }

        return null;
    }

    /// <summary>The plan a surface is derived from, and which part of it: the floor (-1) or a segment index.</summary>
    public FloorPlan? FindFloorPlanOf(Guid surfaceId, out int segment)
    {
        segment = -1;
        if (surfaceId == Guid.Empty)
            return null;

        foreach (var plan in FloorPlans)
        {
            // A lowered surface is linked but free: it no longer follows the plan.
            if (plan.LoweredSurfaceIds.Contains(surfaceId))
                continue;

            if (plan.FloorSurfaceId == surfaceId)
                return plan;

            for (var i = 0; i < plan.WallSurfaceIds.Count; i++)
            {
                if (plan.WallSurfaceIds[i] != surfaceId)
                    continue;

                segment = i;
                return plan;
            }
        }

        return null;
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

        writer.WritePropertyName("ContentSources");
        writer.WriteStartArray();
        foreach (var source in ContentSources)
            source.WriteToJson(writer);

        writer.WriteEndArray();

        writer.WritePropertyName("Slices");
        writer.WriteStartArray();
        foreach (var slice in Slices)
            slice.WriteToJson(writer);

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

        if (FloorPlans.Count > 0)
        {
            writer.WritePropertyName("FloorPlans");
            writer.WriteStartArray();
            foreach (var plan in FloorPlans)
                plan.WriteToJson(writer);

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    public static Setup ReadFromJson(JToken token)
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
                       ContentSources = token.ReadListSafe("ContentSources", ContentSource.ReadFromJson),
                       Slices = token.ReadListSafe("Slices", Slice.ReadFromJson),
                       Outputs = token.ReadListSafe("Outputs", OutputDefinition.ReadFromJson),
                       Props = token.ReadListSafe("Props", Prop.ReadFromJson),
                       FloorPlans = token.ReadListSafe("FloorPlans", FloorPlan.ReadFromJson),
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

    /// <summary>The output a surface is mapped to, following a region up to whichever ancestor carries the pin.</summary>
    public bool TryGetOutputOfSurface(Guid surfaceId, out Guid outputId)
    {
        var carrier = FindMappedAncestor(surfaceId);
        if (carrier == null || carrier.OutputMappings.Count == 0)
        {
            outputId = Guid.Empty;
            return false;
        }

        outputId = carrier.OutputMappings[0].OutputId;
        return true;
    }

    /// <summary>The output a slice reaches: a patch showing it directly, else a surface that shows it.</summary>
    public bool TryGetOutputOfSlice(Guid sliceId, out Guid outputId)
    {
        outputId = Guid.Empty;
        foreach (var output in Outputs)
        {
            if (!output.ShowsSlice(sliceId))
                continue;

            outputId = output.Id;
            return true;
        }

        foreach (var surface in Surfaces)
        {
            if (surface.SliceId == sliceId && TryGetOutputOfSurface(surface.Id, out outputId))
                return true;
        }

        return false;
    }

    /// <summary>The output a send op's content reaches first, through any of the slices cut from it.</summary>
    public bool TryGetOutputOfSend(Guid symbolChildId, out Guid outputId)
    {
        outputId = Guid.Empty;
        var source = FindSourceByChildId(symbolChildId);
        if (source == null)
            return false;

        foreach (var slice in Slices)
        {
            if (slice.SourceId == source.Id && TryGetOutputOfSlice(slice.Id, out outputId))
                return true;
        }

        return false;
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

    /// <summary>
    /// Loads and runs <see cref="SetupRepair"/> on the result, so every loader gets sane data.
    /// <paramref name="wasRepaired"/> tells the caller to persist the repaired setup.
    /// </summary>
    public static bool TryLoadFromFile(string filePath, [NotNullWhen(true)] out Setup? setup, out bool wasRepaired)
    {
        setup = null;
        wasRepaired = false;
        try
        {
            var json = File.ReadAllText(filePath);
            setup = ReadFromJson(JObject.Parse(json));
        }
        catch (Exception e)
        {
            Log.Warning($"Can't load setup from {filePath}: {e.Message}");
            return false;
        }

        wasRepaired = SetupRepair.Repair(setup);
        return true;
    }
}
