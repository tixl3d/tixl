#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace T3.Editor.Gui.Windows.TimeLine;

/// <summary>
/// Persisted per-symbol timeline state. Stored under "Timeline" in the .t3ui Settings block.
/// </summary>
internal sealed class TimelineState
{
    // View
    public float ScaleX = 1;
    public float ScrollX;

    // Mode
    [JsonConverter(typeof(StringEnumConverter))]
    public TimeLineCanvas.Modes Mode = TimeLineCanvas.Modes.DopeView;

    #region Serialization

    internal void WriteToJson(JsonTextWriter writer)
    {
        writer.WritePropertyName("Timeline");
        writer.WriteRawValue(JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    internal static TimelineState? ReadFromJson(JToken? settingsToken)
    {
        if (settingsToken == null)
            return null;

        var token = settingsToken["Timeline"];
        return token?.ToObject<TimelineState>();
    }

    #endregion
}
