using System;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// Annotation primitive: a measuring line, or a reference point (the Kind discriminator keeps room for
/// markers and text). Coordinates are in the owner's space: reference-image pixels on a
/// <see cref="ReferenceBinding"/>, surface metres on a <see cref="Surface"/>. An optional physical length
/// turns a line into a measurement. A point uses <see cref="P1"/> only (<see cref="P2"/> mirrors it).
/// </summary>
public sealed class LineAnnotation
{
    public static class Kinds
    {
        public const string Line = "Line";

        /// <summary>A named spot on the surface — a physical feature to calibrate against (a corner, an outlet).</summary>
        public const string Point = "Point";
    }

    public bool IsPoint => Kind == Kinds.Point;

    /// <summary>Stable identity, so an output can remember where a point was aimed (<see cref="Surface.OutputMapping.PointTargets"/>).</summary>
    public Guid Id = Guid.NewGuid();

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
        writer.WriteObject("Id", Id);
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
        var id = OutputJson.ReadGuid(token["Id"]);
        return new LineAnnotation
                   {
                       Id = id == Guid.Empty ? Guid.NewGuid() : id,
                       Kind = token.ReadValueSafe("Kind", Kinds.Line) ?? Kinds.Line,
                       P1 = OutputJson.ReadVector2(token["P1"]),
                       P2 = OutputJson.ReadVector2(token["P2"]),
                       LengthInMeters = token.ReadValueSafe("LengthInMeters", 0f),
                       Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                       ShowArrows = token.ReadValueSafe("ShowArrows", false),
                   };
    }
}
