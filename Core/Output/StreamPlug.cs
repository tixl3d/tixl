#nullable enable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// A stream sender this machine offers as a plug next to its displays — a Spout or NDI sender under a chosen
/// name. Part of the <see cref="MachineConfig"/> inventory (per computer, like displays), so an output bound to
/// it stays venue-agnostic; the sender only runs while an output is bound and sending.
/// </summary>
public sealed class StreamPlug
{
    public Guid Id = Guid.NewGuid();

    /// <summary>The <see cref="IOutputStreamProvider.Kind"/> that drives it.</summary>
    public string Kind = string.Empty;

    /// <summary>The sender name receivers see.</summary>
    public string Name = string.Empty;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteString("Kind", Kind);
        writer.WriteString("Name", Name);
        writer.WriteEndObject();
    }

    public static StreamPlug ReadFromJson(JToken token)
    {
        return new StreamPlug
                   {
                       Id = OutputJson.ReadGuid(token["Id"]),
                       Kind = token.ReadValueSafe("Kind", string.Empty) ?? string.Empty,
                       Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                   };
    }
}
