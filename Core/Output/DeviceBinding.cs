using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// Per-machine record connecting an Output to a physical display — the "which connector on
/// which computer" part, deliberately outside the Setup so venue files stay machine-agnostic.
/// Displays are matched by name first, index as fallback; a failed match must surface as
/// explicitly unresolved, never silently re-matched.
/// </summary>
public sealed class DeviceBinding
{
    public Guid OutputId;

    /// <summary>OS device name (e.g. \\.\DISPLAY2) — the stable identity across reboots.</summary>
    public string DisplayName = string.Empty;

    /// <summary>Fallback when the name no longer matches (displays renamed/replugged).</summary>
    public int DisplayIndex;

    public bool Fullscreen = true;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("OutputId", OutputId);
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
                       DisplayName = token.ReadValueSafe("DisplayName", string.Empty) ?? string.Empty,
                       DisplayIndex = token.ReadValueSafe("DisplayIndex", 0),
                       Fullscreen = token.ReadValueSafe("Fullscreen", true),
                   };
    }
}
