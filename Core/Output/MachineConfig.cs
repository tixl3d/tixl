#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Core.Utils;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// The machine-specific side of the output pipeline: the stream plugs this machine offers, device
/// bindings and (later) window placement and sync. Lives next to the setups in the project's .meta/Setups/
/// folder but is per-computer and meant to be gitignored — a touring show rewrites it on first bind
/// at each venue while Setup and project never learn about display numbering.
/// </summary>
public sealed class MachineConfig
{
    public const int CurrentVersion = 1;
    public const string FileName = "outputs.machine.json";

    public List<PlugBinding> Bindings = [];

    /// <summary>
    /// The setup this machine last had active for the project. Per-machine because the venue a
    /// computer stands in is machine state, not project content.
    /// </summary>
    public string ActiveSetupName = string.Empty;

    /// <summary>Stream senders this machine offers as plugs, next to its displays.</summary>
    public List<StreamPlug> StreamPlugs = [];

    /// <summary>Laser devices this machine can send to as plugs, next to its displays and streams.</summary>
    public List<LaserPlug> LaserPlugs = [];

    public StreamPlug? FindStreamPlug(Guid plugId)
    {
        foreach (var stream in StreamPlugs)
        {
            if (stream.Id == plugId)
                return stream;
        }

        return null;
    }

    /// <summary>
    /// Re-homes laser plugs whose LocalAddress names an adapter this machine does not own — what a
    /// project folder cloned from another computer carries. The file is per-machine by design, but a
    /// folder copy (unlike a boygroup deploy or a collab download, which exclude it) brings the
    /// source machine's copy along, and streams would then keep retrying a bind to an address that
    /// is not ours. The plug moves to the local adapter that reaches its device (same subnet), or to
    /// "any" when nothing pins the subnet down. Returns the number of plugs changed; the caller
    /// persists when it is above zero.
    /// </summary>
    public int ReconcileLocalInterfaces()
    {
        var changed = 0;
        foreach (var plug in LaserPlugs)
        {
            var replacement = LanInterfaces.ReconcileLocalAddress(plug.LocalAddress, plug.Address);
            if (string.Equals(replacement, plug.LocalAddress, StringComparison.Ordinal))
                continue;

            Log.Info($"Laser plug \"{plug.Name}\": cloned local address {plug.LocalAddress} "
                     + (replacement.Length > 0 ? $"re-homed to this machine's adapter {replacement}."
                                               : "cleared to any interface (no local adapter in the device's subnet)."));
            plug.LocalAddress = replacement;
            changed++;
        }

        return changed;
    }

    public LaserPlug? FindLaserPlug(Guid plugId)
    {
        foreach (var laser in LaserPlugs)
        {
            if (laser.Id == plugId)
                return laser;
        }

        return null;
    }

    /// <summary>Removes a stream plug and every binding into it.</summary>
    public void RemoveStream(Guid plugId)
    {
        StreamPlugs.RemoveAll(s => s.Id == plugId);
        Bindings.RemoveAll(b => b.IsStream && b.PlugId == plugId);
    }

    /// <summary>Removes a laser plug and every binding into it.</summary>
    public void RemoveLaser(Guid plugId)
    {
        LaserPlugs.RemoveAll(l => l.Id == plugId);
        Bindings.RemoveAll(b => b.IsLaser && b.PlugId == plugId);
    }

    /// <summary>
    /// Drops bindings whose device kind disagrees with the kind of the output they name: a display or
    /// stream binding on a laser output (which would present the input full-screen on that display -
    /// a laser output has no canvas, its frame is vector content streamed by the laser driver), and a
    /// laser binding on a canvas output. A config can carry such a pair when an output's kind
    /// changed after the binding was made; the presentation pass skips them, but removing them
    /// keeps the board and the status honest.
    /// Returns the number of bindings removed.
    /// </summary>
    public int RemoveKindMismatchedBindings(IEnumerable<OutputDefinition> outputs)
    {
        var laserOutputIds = new HashSet<Guid>();
        var canvasOutputIds = new HashSet<Guid>();
        foreach (var output in outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Laser)
                laserOutputIds.Add(output.Id);
            else
                canvasOutputIds.Add(output.Id);
        }

        return Bindings.RemoveAll(b => b.IsLaser ? canvasOutputIds.Contains(b.OutputId) : laserOutputIds.Contains(b.OutputId));
    }

    public PlugBinding? FindBinding(Guid outputId)
    {
        foreach (var binding in Bindings)
        {
            if (binding.OutputId == outputId)
                return binding;
        }

        return null;
    }

    /// <summary>All bindings of a laser output - it can fan the same frame out to several devices
    /// (the same show on two scanners). Other output kinds stay single-bound: a display would be
    /// fought over and a stream would carry two canvases through one sender.</summary>
    public IEnumerable<PlugBinding> FindLaserBindings(Guid outputId)
    {
        foreach (var binding in Bindings)
        {
            if (binding.OutputId == outputId && binding.IsLaser)
                yield return binding;
        }
    }

    /// <summary>Adds or replaces the binding for an output. Laser bindings accumulate - rebinding the
    /// same plug replaces that one binding; the output's other plugs keep theirs. Non-laser outputs
    /// stay single-bound.</summary>
    public void Bind(PlugBinding binding)
    {
        if (binding.IsLaser)
        {
            Bindings.RemoveAll(b => b.IsLaser && b.OutputId == binding.OutputId && b.PlugId == binding.PlugId);
        }
        else
        {
            Bindings.RemoveAll(b => b.OutputId == binding.OutputId);
        }

        Bindings.Add(binding);
    }

    public void Unbind(Guid outputId)
    {
        Bindings.RemoveAll(b => b.OutputId == outputId);
    }

    /// <summary>Removes exactly the output's binding to one plug - how a multi-bound laser output
    /// lets go of one of its devices while keeping the rest.</summary>
    public void Unbind(Guid outputId, Guid plugId)
    {
        Bindings.RemoveAll(b => b.OutputId == outputId && b.PlugId == plugId);
    }

    public string ToJsonString()
    {
        var sb = new StringBuilder();
        using (var stringWriter = new StringWriter(sb))
        using (var writer = new JsonTextWriter(stringWriter))
        {
            writer.Formatting = Formatting.Indented;
            writer.WriteStartObject();
            writer.WriteValue("Version", CurrentVersion);
            if (ActiveSetupName.Length > 0)
                writer.WriteString("ActiveSetupName", ActiveSetupName);

            writer.WritePropertyName("Bindings");
            writer.WriteStartArray();
            foreach (var binding in Bindings)
                binding.WriteToJson(writer);

            writer.WriteEndArray();
            if (StreamPlugs.Count > 0)
            {
                writer.WritePropertyName("Streams");
                writer.WriteStartArray();
                foreach (var stream in StreamPlugs)
                    stream.WriteToJson(writer);

                writer.WriteEndArray();
            }

            if (LaserPlugs.Count > 0)
            {
                writer.WritePropertyName("Lasers");
                writer.WriteStartArray();
                foreach (var laser in LaserPlugs)
                    laser.WriteToJson(writer);

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.Flush();
        }

        return sb.ToString();
    }

    public static MachineConfig ReadFromJson(JToken token)
    {
        var version = token.ReadValueSafe("Version", 0);
        if (version > CurrentVersion)
            Log.Warning($"Machine output config has format v{version} > v{CurrentVersion} — loading what we can.");

        return new MachineConfig
                   {
                       ActiveSetupName = token.ReadValueSafe("ActiveSetupName", string.Empty) ?? string.Empty,
                       Bindings = token.ReadListSafe("Bindings", PlugBinding.ReadFromJson),
                       StreamPlugs = token.ReadListSafe("Streams", StreamPlug.ReadFromJson),
                       LaserPlugs = token.ReadListSafe("Lasers", LaserPlug.ReadFromJson),
                   };
    }

    public bool TrySaveToFile(string filePath)
    {
        try
        {
            SetupFiles.WriteFileAtomically(filePath, ToJsonString());
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Can't save machine output config to {filePath}: {e.Message}");
            return false;
        }
    }

    public static bool TryLoadFromFile(string filePath, out MachineConfig config)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            config = ReadFromJson(JObject.Parse(json));
            return true;
        }
        catch (Exception e)
        {
            Log.Warning($"Can't load machine output config from {filePath}: {e.Message}");
            config = new MachineConfig();
            KeepCopyOfUnreadableFile(filePath);
            return false;
        }
    }

    /** The next save replaces the file with the empty config, which would erase the bindings and stream plugs it may still hold. */
    private static void KeepCopyOfUnreadableFile(string filePath)
    {
        var copyPath = filePath + ".unreadable";
        try
        {
            File.Copy(filePath, copyPath, overwrite: true);
            Log.Warning($"The unreadable machine output config was kept as {copyPath}");
        }
        catch (Exception e)
        {
            Log.Warning($"Can't keep a copy of the unreadable machine output config {filePath}: {e.Message}");
        }
    }
}
