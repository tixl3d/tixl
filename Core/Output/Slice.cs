#nullable enable
using System;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// A rectangle taken from a <see cref="ContentSource"/> — the unit a surface actually shows. Surfaces
/// reference a slice rather than an op, so routing is setup data: it survives re-instantiation, duplicates
/// with the setup, and is edited where the rest of the calibration is.
/// <para>Using a whole image is not a separate case; it is a slice whose rect is the full (0,0)-(1,1).</para>
/// </summary>
public sealed class Slice
{
    public Guid Id = Guid.NewGuid();

    /// <summary>The source this is cut from.</summary>
    public Guid SourceId;

    public string Name = string.Empty;

    /// <summary>Sub-rect of the source in UV: (xMin, yMin, xMax, yMax). Full source = (0,0,1,1).</summary>
    public Vector4 UvRect = new(0, 0, 1, 1);

    /// <summary>Rotation of the slice within the source, in degrees, about the rect's centre.</summary>
    public float Rotation;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteObject("SourceId", SourceId);
        writer.WriteString("Name", Name);
        writer.WriteVector4("UvRect", UvRect);
        writer.WriteValue("Rotation", Rotation);
        writer.WriteEndObject();
    }

    public static Slice ReadFromJson(JToken token)
    {
        return new Slice
                   {
                       Id = OutputJson.ReadGuid(token["Id"]),
                       SourceId = OutputJson.ReadGuid(token["SourceId"]),
                       Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                       UvRect = OutputJson.ReadVector4(token["UvRect"], new Vector4(0, 0, 1, 1)),
                       Rotation = token.ReadValueSafe("Rotation", 0f),
                   };
    }
}
