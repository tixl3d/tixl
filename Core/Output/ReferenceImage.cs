using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// Image used to define and measure surfaces. Photos are perspective and need straightening;
/// plans are orthographic drawings that only need a scale.
/// </summary>
public sealed class ReferenceImage
{
    /// <summary>String discriminator so new kinds don't break old readers.</summary>
    public static class Kinds
    {
        public const string Photo = "Photo";
        public const string Plan = "Plan";
    }

    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public string Kind = Kinds.Photo;

    /// <summary>Project-relative asset path — setups stay portable with the project.</summary>
    public string FilePath = string.Empty;

    public int Width;
    public int Height;

    /// <summary>Scale for plans; ignored for photos (their scale comes from annotations).</summary>
    public float MetersPerPixel;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteString("Name", Name);
        writer.WriteString("Kind", Kind);
        writer.WriteString("FilePath", FilePath);
        writer.WriteValue("Width", Width);
        writer.WriteValue("Height", Height);
        if (MetersPerPixel > 0)
            writer.WriteValue("MetersPerPixel", MetersPerPixel);

        writer.WriteEndObject();
    }

    public static ReferenceImage ReadFromJson(JToken token)
    {
        return new ReferenceImage
                   {
                       Id = OutputJson.ReadGuid(token["Id"]),
                       Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                       Kind = token.ReadValueSafe("Kind", Kinds.Photo) ?? Kinds.Photo,
                       FilePath = token.ReadValueSafe("FilePath", string.Empty) ?? string.Empty,
                       Width = token.ReadValueSafe("Width", 0),
                       Height = token.ReadValueSafe("Height", 0),
                       MetersPerPixel = token.ReadValueSafe("MetersPerPixel", 0f),
                   };
    }
}
