using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// Per-machine record connecting an Output to a plug — a physical display or a stream sender — the "which
/// connector on which computer" part, deliberately outside the Setup so venue files stay machine-agnostic.
/// Displays are matched by name first, index as fallback; a failed match must surface as
/// explicitly unresolved, never silently re-matched.
/// </summary>
public sealed class DeviceBinding
{
    public static class Kinds
    {
        public const string Display = "Display";
        public const string Stream = "Stream";
    }

    public Guid OutputId;

    /// <summary>A display (index/name below) or a <see cref="StreamPlug"/> (<see cref="PlugId"/>).</summary>
    public string Kind = Kinds.Display;

    /// <summary>The stream plug for <see cref="Kinds.Stream"/> bindings; unused for displays.</summary>
    public Guid PlugId;

    public bool IsStream => Kind == Kinds.Stream;

    /// <summary>OS device name (e.g. \\.\DISPLAY2) — the stable identity across reboots.</summary>
    public string DisplayName = string.Empty;

    /// <summary>Fallback when the name no longer matches (displays renamed/replugged).</summary>
    public int DisplayIndex;

    public bool Fullscreen = true;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("OutputId", OutputId);
        writer.WriteString("Kind", Kind);
        if (PlugId != Guid.Empty)
            writer.WriteObject("PlugId", PlugId);

        writer.WriteString("DisplayName", DisplayName);
        writer.WriteValue("DisplayIndex", DisplayIndex);
        writer.WriteValue("Fullscreen", Fullscreen);
        writer.WriteEndObject();
    }

    public static DeviceBinding ReadFromJson(JToken token)
    {
        return new DeviceBinding
                   {
                       OutputId = OutputJson.ReadGuid(token["OutputId"]),
                       Kind = token.ReadValueSafe("Kind", Kinds.Display) ?? Kinds.Display,
                       PlugId = token["PlugId"] == null ? Guid.Empty : OutputJson.ReadGuid(token["PlugId"]),
                       DisplayName = token.ReadValueSafe("DisplayName", string.Empty) ?? string.Empty,
                       DisplayIndex = token.ReadValueSafe("DisplayIndex", 0),
                       Fullscreen = token.ReadValueSafe("Fullscreen", true),
                   };
    }
}
