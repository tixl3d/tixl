#nullable enable
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// Where an entity's card sits on the Board — the 2D unfolded overview in metres, Y up, floor at y = 0.
/// Presentation only: moving or scaling a card never touches calibration, routing or resolution. Absent
/// until the Board seeds it, so setups that never opened the Board carry no layout.
/// </summary>
public sealed class BoardPlacement
{
    /// <summary>Board metres: a surface's anchor, or a pixel card's bottom-left corner.</summary>
    public Vector2 Position;

    /// <summary>Presentation scale of a pixel entity (content, output, reference image); 0 = the Board's default.</summary>
    public float CardScale;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteVector2("Position", Position);
        if (CardScale > 0)
            writer.WriteValue("PixelsPerMeter", CardScale);

        writer.WriteEndObject();
    }

    public static BoardPlacement ReadFromJson(JToken token)
    {
        return new BoardPlacement
                   {
                       Position = OutputJson.ReadVector2(token["Position"]),
                       CardScale = token.ReadValueSafe("PixelsPerMeter", 0f),
                   };
    }
}
