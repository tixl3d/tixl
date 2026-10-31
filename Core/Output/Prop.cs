using System;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>Scale/occlusion reference in the stage or projected into reference views (e.g. a 1.70 m person).</summary>
public sealed class Prop
{
    public static class Kinds
    {
        public const string Person = "Person";
    }

    public Guid Id = Guid.NewGuid();
    public string Kind = Kinds.Person;
    public Vector3 Position;
    public float HeightInMeters = 1.70f;

    /// <summary>The image it is drawn with, relative to the editor's props folder. Empty = the editor's first one.</summary>
    public string ImagePath = string.Empty;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteString("Kind", Kind);
        writer.WriteVector3("Position", Position);
        writer.WriteValue("HeightInMeters", HeightInMeters);
        if (!string.IsNullOrEmpty(ImagePath))
            writer.WriteString("ImagePath", ImagePath);

        writer.WriteEndObject();
    }

    public static Prop ReadFromJson(JToken token)
    {
        return new Prop
                   {
                       Id = OutputJson.ReadGuid(token["Id"]),
                       Kind = token.ReadValueSafe("Kind", Kinds.Person) ?? Kinds.Person,
                       Position = OutputJson.ReadVector3(token["Position"]),
                       HeightInMeters = token.ReadValueSafe("HeightInMeters", 1.70f),
                       ImagePath = token.ReadValueSafe("ImagePath", string.Empty) ?? string.Empty,
                   };
    }
}
