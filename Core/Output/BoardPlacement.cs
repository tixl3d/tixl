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

    /// <summary>
    /// Presentation scale of a pixel entity (content, output, reference image) in card pixels per board metre; 0 =
    /// the Board's default. A reference image's card pixels are its own. A content or output card is
    /// <see cref="NominalCardWidth"/> card pixels wide whatever it renders at, with its aspect for the height, so a
    /// resolution change (a display plugged in, a send following its surface) reshapes the card but never resizes it.
    /// </summary>
    public float CardScale;

    /// <summary>The width, in card pixels, of a content or output card, whatever its resolution.</summary>
    public const float NominalCardWidth = 1920;

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
