#nullable enable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// A source of pixels in the setup, standing 1:1 for the op that supplies them. The op is identified by its
/// <see cref="SymbolChildId"/> — the durable graph entity — rather than by a live instance, which comes and
/// goes with hot-reloads and with whichever part of the graph happens to be instantiated. A source with no
/// live instance simply has no pixels this frame; a source whose op is *deleted* is removed along with its
/// slices and every reference to them.
/// </summary>
public sealed class ContentSource
{
    public Guid Id = Guid.NewGuid();

    /// <summary>The SendToOutput op supplying this source. One op, one source.</summary>
    public Guid SymbolChildId;

    /// <summary>Mirrors the op's name, so the setup still reads sensibly while nothing is instantiated.</summary>
    public string Name = string.Empty;

    /// <summary>Whether the op carries a name the user gave it, rather than falling back to its symbol's
    /// default. Auto-named slices read as "Slice N" until their source is named, then as "{Name}.N".</summary>
    public bool IsRenamed;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteObject("SymbolChildId", SymbolChildId);
        writer.WriteString("Name", Name);
        if (IsRenamed)
            writer.WriteValue("IsRenamed", IsRenamed);

        writer.WriteEndObject();
    }

    public static ContentSource ReadFromJson(JToken token)
    {
        return new ContentSource
                   {
                       Id = OutputJson.ReadGuid(token["Id"]),
                       SymbolChildId = OutputJson.ReadGuid(token["SymbolChildId"]),
                       Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                       IsRenamed = token.ReadValueSafe("IsRenamed", false),
                   };
    }
}
