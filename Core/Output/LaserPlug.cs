#nullable enable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// A laser device this machine can send to as a plug next to its displays and streams — an EtherDream DAC
/// on the LAN (IP address), a PONK endpoint (MadMapper's laser-over-UDP, usually the 239.255.10.24:5583
/// multicast group) or a CITP laser feed consumer. Part of the <see cref="MachineConfig"/> inventory, so a
/// show rebinds its laser outputs to the devices found at the venue without touching the versioned setup.
/// Per-protocol device settings (scan rate, brightness ceiling, channel order…) live here next to the
/// address — the machine-side counterpart of the output-side laser processing settings.
/// </summary>
public sealed class LaserPlug
{
    public static class Kinds
    {
        public const string EtherDream = "EtherDream";
        public const string Ponk = "PONK";
        public const string Citp = "CITP";
        public const string ShowNet = "ShowNet";
    }

    public Guid Id = Guid.NewGuid();

    /// <summary>Which protocol drives the device; decides which output op can bind here.</summary>
    public string Kind = Kinds.EtherDream;

    /// <summary>The name the binding menu shows.</summary>
    public string Name = string.Empty;

    /// <summary>The DAC's IP address (EtherDream), the PONK host / multicast group, or the CITP capture host.</summary>
    public string Address = string.Empty;

    /// <summary>Remote port; 0 = the protocol's default (PONK 5583; CITP 0 = multicast discovery).</summary>
    public int Port;

    /// <summary>Network interface packets leave on (IP of this machine's NIC); empty = any.</summary>
    public string LocalAddress = string.Empty;

    /// <summary>Per-protocol device settings; null = defaults. Only the block for <see cref="Kind"/> is used.</summary>
    public EtherDreamDeviceSettings? EtherDream;
    public PonkDeviceSettings? Ponk;
    public CitpDeviceSettings? Citp;
    public ShowNetDeviceSettings? ShowNet;

    public EtherDreamDeviceSettings EtherDreamSettings => EtherDream ??= new EtherDreamDeviceSettings();
    public PonkDeviceSettings PonkSettings => Ponk ??= new PonkDeviceSettings();
    public CitpDeviceSettings CitpSettings => Citp ??= new CitpDeviceSettings();
    public ShowNetDeviceSettings ShowNetSettings => ShowNet ??= new ShowNetDeviceSettings();

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteString("Kind", Kind);
        writer.WriteString("Name", Name);
        writer.WriteString("Address", Address);
        writer.WriteString("LocalAddress", LocalAddress);
        if (Port != 0)
            writer.WriteValue("Port", Port);

        if (EtherDream != null)
        {
            writer.WritePropertyName("EtherDream");
            EtherDream.WriteToJson(writer);
        }

        if (Ponk != null)
        {
            writer.WritePropertyName("Ponk");
            Ponk.WriteToJson(writer);
        }

        if (Citp != null)
        {
            writer.WritePropertyName("Citp");
            Citp.WriteToJson(writer);
        }

        if (ShowNet != null)
        {
            writer.WritePropertyName("ShowNet");
            ShowNet.WriteToJson(writer);
        }

        writer.WriteEndObject();
    }

    public static LaserPlug ReadFromJson(JToken token)
    {
        return new LaserPlug
                   {
                       Id = OutputJson.ReadGuid(token["Id"]),
                       Kind = token.ReadValueSafe("Kind", Kinds.EtherDream) ?? Kinds.EtherDream,
                       Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                       Address = token.ReadValueSafe("Address", string.Empty) ?? string.Empty,
                       LocalAddress = token.ReadValueSafe("LocalAddress", string.Empty) ?? string.Empty,
                       Port = token.ReadValueSafe("Port", 0),
                       EtherDream = token["EtherDream"] is JObject ed ? EtherDreamDeviceSettings.ReadFromJson(ed) : null,
                       Ponk = token["Ponk"] is JObject po ? PonkDeviceSettings.ReadFromJson(po) : null,
                       Citp = token["Citp"] is JObject ci ? CitpDeviceSettings.ReadFromJson(ci) : null,
                       ShowNet = token["ShowNet"] is JObject sn ? ShowNetDeviceSettings.ReadFromJson(sn) : null,
                   };
    }
}

/// <summary>EtherDream DAC device settings — the knobs of the hardware, not of the show.</summary>
public sealed class EtherDreamDeviceSettings
{
    /// <summary>Points per second requested from the DAC; 0 = the output op's own input applies.</summary>
    public int ScanRate;

    /// <summary>Final intensity ceiling (0..1) applied at encode — the non-bypassable device audit.</summary>
    public float MaxBrightness = 1f;

    /// <summary>Some projectors expect the color channels as BGR.</summary>
    public bool SwapColorChannels;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        if (ScanRate != 0)
            writer.WriteValue("ScanRate", ScanRate);

        writer.WriteValue("MaxBrightness", MaxBrightness);
        writer.WriteValue("SwapColorChannels", SwapColorChannels);
        writer.WriteEndObject();
    }

    public static EtherDreamDeviceSettings ReadFromJson(JToken token)
    {
        return new EtherDreamDeviceSettings
                   {
                       ScanRate = token.ReadValueSafe("ScanRate", 0),
                       MaxBrightness = token.ReadValueSafe("MaxBrightness", 1f),
                       SwapColorChannels = token.ReadValueSafe("SwapColorChannels", false),
                   };
    }
}

/// <summary>PONK receiver device settings.</summary>
public sealed class PonkDeviceSettings
{
    public bool UseMulticast = true;

    /// <summary>The sender name receivers see.</summary>
    public string SenderName = "TiXL";

    /// <summary>Sent as MAXSPEED path metadata — requested scanner-speed multiplier.</summary>
    public float MaxScanSpeed = 1f;

    /// <summary>Sender identity in every packet header - persisted, so receivers (MadMapper keys
    /// its Ponk inputs on it) recognize this plug across editor restarts instead of seeing a new
    /// sender per session. 0 = generated on first use and written back here.</summary>
    public int SenderId;

    public float MaxBrightness = 1f;
    public bool SwapColorChannels;

    /// <summary>Keep streaming while the LASER master is off. PONK receivers can drive real
    /// scanners, so this defaults to off - the master stays a hard kill; enabling it is a
    /// deliberate preview override.</summary>
    public bool Keepalive;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteValue("UseMulticast", UseMulticast);
        writer.WriteString("SenderName", SenderName);
        writer.WriteValue("MaxScanSpeed", MaxScanSpeed);
        writer.WriteValue("SenderId", SenderId);
        writer.WriteValue("MaxBrightness", MaxBrightness);
        writer.WriteValue("SwapColorChannels", SwapColorChannels);
        writer.WriteValue("Keepalive", Keepalive);
        writer.WriteEndObject();
    }

    public static PonkDeviceSettings ReadFromJson(JToken token)
    {
        return new PonkDeviceSettings
                   {
                       UseMulticast = token.ReadValueSafe("UseMulticast", true),
                       SenderName = token.ReadValueSafe("SenderName", "TiXL") ?? "TiXL",
                       MaxScanSpeed = token.ReadValueSafe("MaxScanSpeed", 1f),
                       SenderId = token.ReadValueSafe("SenderId", 0),
                       MaxBrightness = token.ReadValueSafe("MaxBrightness", 1f),
                       SwapColorChannels = token.ReadValueSafe("SwapColorChannels", false),
                       Keepalive = token.ReadValueSafe("Keepalive", false),
                   };
    }
}

/// <summary>CITP laser feed device settings.</summary>
public sealed class CitpDeviceSettings
{
    /// <summary>Feed name sent to the capture ("FeedName i" per connected feed). Empty - or the
    /// pre-4.4 default "Laser Feed" - follows the plug's name, so several TiXL laser outputs appear
    /// as distinct feeds in the capture without any per-plug setup.</summary>
    public string FeedName = string.Empty;

    /// <summary>Kept so saved setups load; the shared CITP source generates one sender key per
    /// lifetime and no longer reads this per-plug value.</summary>
    public uint SourceKey;

    /// <summary>The feed name the stream advertises for this plug: the explicit name, or - when
    /// empty or left at the pre-4.4 default "Laser Feed" - the plug's own name, so several TiXL
    /// laser outputs appear as distinct feeds in the capture without per-plug setup.</summary>
    public string ResolveFeedName(string plugName)
    {
        if (!string.IsNullOrWhiteSpace(FeedName) && FeedName.Trim() != "Laser Feed")
            return FeedName.Trim();

        return string.IsNullOrWhiteSpace(plugName) ? "Laser Feed" : plugName.Trim();
    }

    public float MaxBrightness = 1f;
    public bool SwapColorChannels;

    /// <summary>Keep the feed alive while the LASER master is off - CITP is a previz path that
    /// never drives projectors, so the visualizer keeps showing the show while the real beam is
    /// dark. Defaults to on.</summary>
    public bool Keepalive = true;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("FeedName", FeedName);
        writer.WriteValue("SourceKey", SourceKey);
        writer.WriteValue("MaxBrightness", MaxBrightness);
        writer.WriteValue("SwapColorChannels", SwapColorChannels);
        writer.WriteValue("Keepalive", Keepalive);
        writer.WriteEndObject();
    }

    public static CitpDeviceSettings ReadFromJson(JToken token)
    {
        return new CitpDeviceSettings
                   {
                       FeedName = token.ReadValueSafe("FeedName", string.Empty)?.Trim() ?? string.Empty,
                       SourceKey = token.ReadValueSafe("SourceKey", 0u),
                       MaxBrightness = token.ReadValueSafe("MaxBrightness", 1f),
                       SwapColorChannels = token.ReadValueSafe("SwapColorChannels", false),
                       Keepalive = token.ReadValueSafe("Keepalive", true),
                   };
    }
}

/// <summary>showNET laser device settings.</summary>
public sealed class ShowNetDeviceSettings
{
    /// <summary>Points per second requested from the device; 0 = the 30 kpps default. Becomes the
    /// frame's ticks-per-point divisor (device clock / scan rate).</summary>
    public int ScanRate;

    /// <summary>Final intensity ceiling (0..1) applied at encode — the non-bypassable device audit.</summary>
    public float MaxBrightness = 1f;

    /// <summary>Some projectors expect the color channels as BGR.</summary>
    public bool SwapColorChannels;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        if (ScanRate != 0)
            writer.WriteValue("ScanRate", ScanRate);

        writer.WriteValue("MaxBrightness", MaxBrightness);
        writer.WriteValue("SwapColorChannels", SwapColorChannels);
        writer.WriteEndObject();
    }

    public static ShowNetDeviceSettings ReadFromJson(JToken token)
    {
        return new ShowNetDeviceSettings
                   {
                       ScanRate = token.ReadValueSafe("ScanRate", 0),
                       MaxBrightness = token.ReadValueSafe("MaxBrightness", 1f),
                       SwapColorChannels = token.ReadValueSafe("SwapColorChannels", false),
                   };
    }
}
