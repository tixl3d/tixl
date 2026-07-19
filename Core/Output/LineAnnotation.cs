using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// Measuring/annotation primitive on reference images (first of a planned annotation family —
/// the Kind discriminator keeps room for points, markers, text). Endpoints are in
/// reference-image pixels; an optional physical length turns the line into a measurement.
/// </summary>
public sealed class LineAnnotation
{
    public static class Kinds
    {
        public const string Line = "Line";
    }

    public string Kind = Kinds.Line;
    public Vector2 P1;
    public Vector2 P2;

    /// <summary>Physical length in meters; 0 = unmeasured (display-only line).</summary>
    public float LengthInMeters;

    public string Name = string.Empty;
    public bool ShowArrows;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("Kind", Kind);
        writer.WriteVector2("P1", P1);
        writer.WriteVector2("P2", P2);
        if (LengthInMeters > 0)
            writer.WriteValue("LengthInMeters", LengthInMeters);

        if (!string.IsNullOrEmpty(Name))
            writer.WriteString("Name", Name);

        if (ShowArrows)
            writer.WriteValue("ShowArrows", ShowArrows);

        writer.WriteEndObject();
    }

    public static LineAnnotation ReadFromJson(JToken token)
    {
        return new LineAnnotation
                   {
                       Kind = token.ReadValueSafe("Kind", Kinds.Line) ?? Kinds.Line,
                       P1 = OutputJson.ReadVector2(token["P1"]),
                       P2 = OutputJson.ReadVector2(token["P2"]),
                       LengthInMeters = token.ReadValueSafe("LengthInMeters", 0f),
                       Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                       ShowArrows = token.ReadValueSafe("ShowArrows", false),
                   };
    }
}
