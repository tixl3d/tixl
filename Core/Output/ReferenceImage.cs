#nullable enable
using System;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// Image used to define and measure surfaces. Photos are perspective and need straightening;
/// plans are orthographic drawings that only need a scale; pixel maps are a venue's layout of an
/// output canvas, drawn 1:1 over it.
/// </summary>
public sealed class ReferenceImage
{
    /// <summary>String discriminator so new kinds don't break old readers.</summary>
    public static class Kinds
    {
        public const string Photo = "Photo";
        public const string Plan = "Plan";
        public const string PixelMap = "PixelMap";
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

    /// <summary>A known length drawn on the image, in image pixels, from which <see cref="MetersPerPixel"/> is derived.</summary>
    public Vector2 ScaleLineStart;

    public Vector2 ScaleLineEnd;

    /// <summary>The real length of the scale line, in metres.</summary>
    public float ScaleLineMeters;

    public bool HasScaleLine => (ScaleLineEnd - ScaleLineStart).LengthSquared() > 0.25f;

    /// <summary>A locked image is a backdrop: drawn beneath every other card and never picked, grabbed or fenced.</summary>
    public bool IsLocked;

    /// <summary>How strongly the card shows the image on the Board, 0..1 — dimmed, a backdrop stays behind what is drawn over it.</summary>
    public float Opacity = 1f;

    /// <summary>Its card's place on the Board; null until the Board seeded one.</summary>
    public BoardPlacement? BoardPlacement;

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

        if (HasScaleLine)
        {
            writer.WriteVector2("ScaleLineStart", ScaleLineStart);
            writer.WriteVector2("ScaleLineEnd", ScaleLineEnd);
            writer.WriteValue("ScaleLineMeters", ScaleLineMeters);
        }

        if (IsLocked)
            writer.WriteValue("IsLocked", true);

        if (Opacity < 1f)
            writer.WriteValue("Opacity", Opacity);

        if (BoardPlacement != null)
        {
            writer.WritePropertyName("BoardPlacement");
            BoardPlacement.WriteToJson(writer);
        }

        writer.WriteEndObject();
    }

    public static ReferenceImage ReadFromJson(JToken token)
    {
        var image = new ReferenceImage
                        {
                            Id = OutputJson.ReadGuid(token["Id"]),
                            Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                            Kind = token.ReadValueSafe("Kind", Kinds.Photo) ?? Kinds.Photo,
                            FilePath = token.ReadValueSafe("FilePath", string.Empty) ?? string.Empty,
                            Width = token.ReadValueSafe("Width", 0),
                            Height = token.ReadValueSafe("Height", 0),
                            MetersPerPixel = token.ReadValueSafe("MetersPerPixel", 0f),
                            ScaleLineStart = OutputJson.ReadVector2(token["ScaleLineStart"]),
                            ScaleLineEnd = OutputJson.ReadVector2(token["ScaleLineEnd"]),
                            ScaleLineMeters = token.ReadValueSafe("ScaleLineMeters", 0f),
                            IsLocked = token.ReadValueSafe("IsLocked", false),
                            Opacity = Math.Clamp(token.ReadValueSafe("Opacity", 1f), 0f, 1f),
                        };
        if (token["BoardPlacement"] is JObject placement)
            image.BoardPlacement = BoardPlacement.ReadFromJson(placement);

        return image;
    }
}
