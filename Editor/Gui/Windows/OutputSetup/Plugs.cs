#nullable enable
using T3.Editor.SystemUi;
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using T3.Core.Output.Streaming;
using T3.Core.Utils;
using T3.Editor.Show;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.Show;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// This machine's plugs — the things an output can be bound to: the attached displays and the stream senders
/// (Spout, NDI) listed in the <see cref="MachineConfig"/>. Both are addressed by one plug id so the outliner,
/// its connections and drag-drop treat them alike: a display's id is derived from its index, a stream's is
/// its <see cref="StreamPlug.Id"/>. Binding edits go through here and save the machine config.
/// </summary>
internal static class Plugs
{
    #region Binding (kind-agnostic)
    /// <summary>The plug id an output is bound to, or empty.</summary>
    public static Guid BoundPlugId(PlugBinding? binding)
    {
        if (binding == null)
            return Guid.Empty;

        return binding.IsStream || binding.IsLaser ? binding.PlugId : DisplayPlugId(binding.DisplayIndex);
    }

    /// <summary>The name the binding presents to — for status lines ("→ Display 2", "→ Spout: Main").</summary>
    public static string BindingLabel(MachineConfig machineConfig, PlugBinding binding)
    {
        if (binding.IsLaser)
        {
            var laser = machineConfig.FindLaserPlug(binding.PlugId);
            return laser == null ? "missing laser" : $"{laser.Kind}: {laser.Name}";
        }

        if (!binding.IsStream)
            return $"Display {binding.DisplayIndex + 1}";

        var stream = machineConfig.FindStreamPlug(binding.PlugId);
        return stream == null ? "missing stream" : $"{stream.Kind}: {stream.Name}";
    }

    /// <summary>The plug's own name — the display's label, the stream sender's, or the laser device's.</summary>
    public static string PlugName(MachineConfig machineConfig, Guid plugId)
    {
        if (TryGetDisplayIndex(plugId, out var displayIndex))
            return DisplayLabel(displayIndex);

        if (TryGetRemoteDisplayName(plugId, out var remoteName))
            return remoteName;

        return machineConfig.FindStreamPlug(plugId)?.Name
               ?? machineConfig.FindLaserPlug(plugId)?.Name
               ?? "Output";
    }

    /// <summary>A display of a connected render node, listed under Local Bindings next to this machine's own.</summary>
    public static bool IsRemoteDisplay(Guid plugId) => Show.BoygroupManager.Instance.TryGetRemoteDisplayPlug(plugId, out _, out _);

    /// <summary>The "node / display" name of a render node's display; false for any other plug.</summary>
    public static bool TryGetRemoteDisplayName(Guid plugId, out string name)
    {
        name = "";
        if (!Show.BoygroupManager.Instance.TryGetRemoteDisplayPlug(plugId, out var client, out var displayIndex))
            return false;

        var screenName = $"Display {displayIndex + 1}";
        foreach (var screen in client.Screens)
        {
            if (screen.Index != displayIndex)
                continue;

            screenName = screen.Name;
            break;
        }

        name = $"{client.Name} / {screenName}";
        return true;
    }

    /// <summary>Whether a plug id still names an attached display, a stream or a laser this machine offers.</summary>
    public static bool Exists(MachineConfig machineConfig, Guid plugId)
    {
        if (TryGetDisplayIndex(plugId, out var displayIndex))
            return displayIndex < EditorUi.Instance.AllScreens.Count;

        if (IsRemoteDisplay(plugId))
            return true;

        return machineConfig.FindStreamPlug(plugId) != null || machineConfig.FindLaserPlug(plugId) != null;
    }

    /// <summary>The pixels a canvas presented here should have: the display's mode, or a sensible default for a stream.</summary>
    public static Int2 PlugResolution(Guid plugId)
    {
        if (TryGetDisplayIndex(plugId, out var displayIndex))
        {
            var screens = EditorUi.Instance.AllScreens;
            if (displayIndex < screens.Count)
                return new Int2(screens[displayIndex].Bounds.Width, screens[displayIndex].Bounds.Height);
        }

        return new Int2(1920, 1080);
    }

    /// <summary>The output a plug currently presents, or null when nothing is bound to it.</summary>
    public static OutputDefinition? TryGetBoundOutput(Setup setup, MachineConfig machineConfig, Guid plugId)
    {
        foreach (var binding in machineConfig.Bindings)
        {
            if (BoundPlugId(binding) != plugId)
                continue;

            var output = setup.FindOutput(binding.OutputId);
            if (output != null)
                return output;
        }

        return null;
    }

    /// <summary>Binds an output to a plug (display, stream or laser), and saves. A plug presents one
    /// output: binding a new one lets go of the old, or a display would be fought over and a stream
    /// would carry two canvases through one sender, alternating frame by frame. Per-plug only - a
    /// multi-bound laser output keeps its other devices. Laser outputs may bind several plugs (the
    /// same frame to two scanners).</summary>
    public static void BindOutput(MachineConfig machineConfig, Guid outputId, Guid plugId)
    {
        // The kinds on either side must agree. A display or stream plug never carries a laser output
        // (a laser output has no canvas - presenting one would throw its input full-screen on that
        // display), and a laser plug never carries a canvas output. Every binding path funnels
        // through here, so the rule lives here as well as in the drop matrix.
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out _))
            return;

        var output = setup.FindOutput(outputId);
        if (output == null)
            return;

        var isLaserOutput = output.Kind == OutputDefinition.Kinds.Laser;
        var isLaserPlug = machineConfig.FindLaserPlug(plugId) != null;
        if (isLaserOutput != isLaserPlug)
            return;

        // A laser plug takes several outputs - two scanners driven by one show, a preview rig - and
        // the laser driver fans the same frame out to each. Canvas plugs stay single-bound: two
        // canvases through one sender would fight over it.
        if (!isLaserPlug)
            RemoveBindingsToPlug(machineConfig, plugId, exceptOutputId: outputId);

        if (TryGetDisplayIndex(plugId, out var displayIndex))
            BindOutputToDisplay(machineConfig, outputId, displayIndex);
        else if (machineConfig.FindLaserPlug(plugId) != null)
            BindOutputToLaser(machineConfig, outputId, plugId);
        else
            BindOutputToStream(machineConfig, outputId, plugId);
    }

    public static void UnbindOutput(MachineConfig machineConfig, Guid outputId)
    {
        machineConfig.Unbind(outputId);
        OutputSetupHandling.SaveActive();
    }

    /// <summary>Drops one of an output's bindings, keeping the others (a laser output's other devices).</summary>
    public static void UnbindOutput(MachineConfig machineConfig, Guid outputId, Guid plugId)
    {
        // Display bindings store no PlugId - it is derived from the display index - so matching the
        // stored id directly would silently keep the binding on screen after an "Unbind".
        RemoveBindingsToPlug(machineConfig, plugId, exceptOutputId: null, onlyOutputId: outputId);
        OutputSetupHandling.SaveActive();
    }

    /// <param name="exceptOutputId">Binding of this output to the plug is kept (rebinding it).</param>
    /// <param name="onlyOutputId">Only this output's binding is removed (unbinding it).</param>
    private static void RemoveBindingsToPlug(MachineConfig machineConfig, Guid plugId, Guid? exceptOutputId = null,
                                             Guid? onlyOutputId = null)
    {
        machineConfig.Bindings.RemoveAll(b => b.OutputId != exceptOutputId
                                              && (onlyOutputId == null || b.OutputId == onlyOutputId)
                                              && BoundPlugId(b) == plugId);
    }
    #endregion

    #region Display plugs
    /// <summary>A stable id per display, so rows, anchors and hover pulses stay put across frames.</summary>
    public static Guid DisplayPlugId(int displayIndex) => new(displayIndex + 1, DisplaySignatureB, DisplaySignatureC, 0, 0, 0, 0, 0, 0, 0, 0);

    public static bool TryGetDisplayIndex(Guid plugId, out int displayIndex)
    {
        Span<byte> bytes = stackalloc byte[16];
        plugId.TryWriteBytes(bytes);
        displayIndex = BitConverter.ToInt32(bytes) - 1;
        var signatureB = BitConverter.ToInt16(bytes.Slice(4, 2));
        var signatureC = BitConverter.ToInt16(bytes.Slice(6, 2));
        var isDisplay = signatureB == DisplaySignatureB && signatureC == DisplaySignatureC && displayIndex >= 0;
        if (!isDisplay)
            displayIndex = -1;

        return isDisplay;
    }

    public static string DisplayLabel(int displayIndex)
    {
        while (_displayLabels.Count <= displayIndex)
            _displayLabels.Add($"Local / Display {_displayLabels.Count + 1}");

        return _displayLabels[displayIndex];
    }

    private static void BindOutputToDisplay(MachineConfig machineConfig, Guid outputId, int displayIndex)
    {
        var screens = EditorUi.Instance.AllScreens;
        machineConfig.Bind(new PlugBinding
                               {
                                   OutputId = outputId,
                                   Kind = PlugBinding.Kinds.Display,
                                   DisplayName = displayIndex < screens.Count ? screens[displayIndex].DeviceName : string.Empty,
                                   DisplayIndex = displayIndex,
                               });
        OutputSetupHandling.SaveActive();
    }

    // Arbitrary markers in the id's second and third fields: they make a display id recognisable and keep it
    // apart from a stream plug's random Guid.
    private const short DisplaySignatureB = 0x5c4e;
    private const short DisplaySignatureC = 0x4e21;
    #endregion

    #region Stream plugs
    /// <summary>Adds a stream plug of a loaded provider's kind under a free name.</summary>
    public static StreamPlug AddStream(MachineConfig machineConfig, string kind)
    {
        var stream = new StreamPlug { Kind = kind, Name = FreeStreamName(machineConfig, kind) };
        machineConfig.StreamPlugs.Add(stream);
        OutputSetupHandling.SaveActive();
        return stream;
    }

    public static void RenameStream(MachineConfig machineConfig, Guid plugId, string newName)
    {
        var stream = machineConfig.FindStreamPlug(plugId);
        if (stream == null || stream.Name == newName)
            return;

        stream.Name = newName;
        OutputSetupHandling.SaveActive();
    }

    /// <summary>Renames a stream or laser plug (displays are named by the OS).</summary>
    public static void RenamePlug(MachineConfig machineConfig, Guid plugId, string newName)
    {
        RenameStream(machineConfig, plugId, newName);
        UpdateLaser(machineConfig, plugId, newName, null, null);
    }

    public static void RemoveStream(MachineConfig machineConfig, Guid plugId)
    {
        machineConfig.RemoveStream(plugId);
        OutputSetupHandling.SaveActive();
    }

    /// <summary>Whether the package that drives this stream kind is loaded right now.</summary>
    public static bool IsStreamKindAvailable(string kind) => OutputStreamRegistry.TryGetProvider(kind) != null;

    private static void BindOutputToStream(MachineConfig machineConfig, Guid outputId, Guid plugId)
    {
        if (machineConfig.FindStreamPlug(plugId) == null)
            return;

        machineConfig.Bind(new PlugBinding
                               {
                                   OutputId = outputId,
                                   Kind = PlugBinding.Kinds.Stream,
                                   PlugId = plugId,
                               });
        OutputSetupHandling.SaveActive();
    }

    private static string FreeStreamName(MachineConfig machineConfig, string kind)
    {
        var baseName = $"TiXL {kind}";
        var name = baseName;
        for (var i = 2; i < 100 && HasStreamNamed(machineConfig, name); i++)
            name = $"{baseName} {i}";

        return name;
    }

    private static bool HasStreamNamed(MachineConfig machineConfig, string name)
    {
        foreach (var stream in machineConfig.StreamPlugs)
        {
            if (stream.Name == name)
                return true;
        }

        return false;
    }
    #endregion

    #region Laser plugs
    /// <summary>Adds a laser plug under a free name — an EtherDream DAC address, a PONK endpoint or a CITP capture.</summary>
    public static LaserPlug AddLaser(MachineConfig machineConfig, string kind)
    {
        var laser = new LaserPlug
                        {
                            Kind = kind,
                            Name = FreeLaserName(machineConfig, kind),
                            Address = kind == LaserPlug.Kinds.Ponk ? "239.255.10.24" : string.Empty,
                            Port = kind == LaserPlug.Kinds.Ponk ? 5583
                               : kind == LaserPlug.Kinds.EtherDream ? 7765
                               : kind == LaserPlug.Kinds.ShowNet ? 41855
                               : 0,
                            // The basic interface: the first usable NIC. Editable per plug; empty
                            // on machines without one ("any interface").
                            LocalAddress = LanInterfaces.GetUsable() is { Count: > 0 } usable
                                               ? usable[0].Address.ToString()
                                               : string.Empty,
                        };
        machineConfig.LaserPlugs.Add(laser);
        OutputSetupHandling.SaveActive();
        return laser;
    }

    public static void UpdateLaser(MachineConfig machineConfig, Guid plugId, string? newName, string? newAddress, int? newPort)
    {
        var laser = machineConfig.FindLaserPlug(plugId);
        if (laser == null)
            return;

        if (newName != null && laser.Name != newName)
            laser.Name = newName;
        if (newAddress != null && laser.Address != newAddress)
            laser.Address = newAddress;
        if (newPort.HasValue && laser.Port != newPort.Value)
            laser.Port = newPort.Value;

        OutputSetupHandling.SaveActive();
    }

    public static void RemoveLaser(MachineConfig machineConfig, Guid plugId)
    {
        machineConfig.RemoveLaser(plugId);
        OutputSetupHandling.SaveActive();
    }

    private static void BindOutputToLaser(MachineConfig machineConfig, Guid outputId, Guid plugId)
    {
        if (machineConfig.FindLaserPlug(plugId) == null)
            return;

        machineConfig.Bind(new PlugBinding
                               {
                                   OutputId = outputId,
                                   Kind = PlugBinding.Kinds.Laser,
                                   PlugId = plugId,
                               });
        OutputSetupHandling.SaveActive();
    }

    private static string FreeLaserName(MachineConfig machineConfig, string kind)
    {
        var baseName = kind == LaserPlug.Kinds.Ponk ? "PONK"
                     : kind == LaserPlug.Kinds.ShowNet ? "showNET"
                     : "Laser";
        var name = baseName;
        for (var i = 2; i < 100 && HasLaserNamed(machineConfig, name); i++)
            name = $"{baseName} {i}";

        return name;
    }

    private static bool HasLaserNamed(MachineConfig machineConfig, string name)
    {
        foreach (var laser in machineConfig.LaserPlugs)
        {
            if (laser.Name == name)
                return true;
        }

        return false;
    }
    #endregion

    private static readonly List<string> _displayLabels = [];
}
