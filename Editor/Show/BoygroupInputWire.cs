#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using T3.Core.IO;

namespace T3.Editor.Show;

/// <summary>
/// Wire format for replicated hardware input (MIDI events and complete OSC packets). Kept in its own
/// plain-JSON file so the headless network tests can exercise the encoding without an editor.
/// </summary>
internal static class BoygroupInputWire
{
    /// <summary>
    /// Upper bound for one message. A dense OSC or MIDI stream is batched once per frame; this keeps a
    /// pathological burst from growing a single frame message without limit.
    /// </summary>
    public const int MaxEventsPerBatch = 4096;

    public static JObject Encode(IReadOnlyList<RemoteMidiEvent> midi, IReadOnlyList<RemoteOscPacket> osc)
    {
        var midiArray = new JArray();
        foreach (var ev in midi)
            midiArray.Add(new JArray(ev.DeviceProductName, ev.RawMessage));

        var oscArray = new JArray();
        foreach (var packet in osc)
        {
            oscArray.Add(new JObject
                             {
                                 ["p"] = packet.Port,
                                 ["d"] = Convert.ToBase64String(packet.Packet),
                             });
        }

        return new JObject
                   {
                       ["t"] = "io",
                       ["midi"] = midiArray,
                       ["osc"] = oscArray,
                   };
    }

    public static bool TryDecode(JObject json, out List<RemoteMidiEvent> midi, out List<RemoteOscPacket> osc)
    {
        midi = [];
        osc = [];

        if (json["midi"] is JArray midiArray)
        {
            foreach (var token in midiArray)
            {
                if (midi.Count >= MaxEventsPerBatch)
                    break;

                if (token is not JArray pair || pair.Count < 2)
                    continue;

                var rawMessage = pair[1]?.Value<int>() ?? 0;
                if (rawMessage == 0)
                    continue;

                midi.Add(new RemoteMidiEvent(pair[0]?.Value<string>() ?? "", rawMessage));
            }
        }

        if (json["osc"] is JArray oscArray)
        {
            foreach (var token in oscArray)
            {
                if (osc.Count >= MaxEventsPerBatch)
                    break;

                if (token is not JObject entry)
                    continue;

                var base64 = entry["d"]?.Value<string>();
                if (string.IsNullOrEmpty(base64))
                    continue;

                try
                {
                    osc.Add(new RemoteOscPacket(entry["p"]?.Value<int>() ?? -1, Convert.FromBase64String(base64)));
                }
                catch (FormatException)
                {
                    // A malformed packet is dropped; the rest of the batch is still applied.
                }
            }
        }

        return midi.Count > 0 || osc.Count > 0;
    }
}
