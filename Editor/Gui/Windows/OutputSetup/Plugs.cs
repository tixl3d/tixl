#nullable enable
using T3.Core.DataTypes.Vector;
using T3.Core.Output;
using T3.Editor.App;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel.ProjectHandling;

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
    public static Guid BoundPlugId(DeviceBinding? binding)
    {
        if (binding == null)
            return Guid.Empty;

        return binding.IsStream ? binding.PlugId : DisplayPlugId(binding.DisplayIndex);
    }

    /// <summary>The name the binding presents to — for status lines ("→ Display 2", "→ Spout: Main").</summary>
    public static string BindingLabel(MachineConfig machineConfig, DeviceBinding binding)
    {
        if (!binding.IsStream)
            return $"Display {binding.DisplayIndex + 1}";

        var stream = machineConfig.FindStream(binding.PlugId);
        return stream == null ? "missing stream" : $"{stream.Kind}: {stream.Name}";
    }

    /// <summary>The plug's own name — the display's label, or the stream sender's name.</summary>
    public static string PlugName(MachineConfig machineConfig, Guid plugId)
    {
        if (TryGetDisplayIndex(plugId, out var displayIndex))
            return DisplayLabel(displayIndex);

        return machineConfig.FindStream(plugId)?.Name ?? "Output";
    }

    /// <summary>The pixels a canvas presented here should have: the display's mode, or a sensible default for a stream.</summary>
    public static Int2 PlugResolution(Guid plugId)
    {
        if (TryGetDisplayIndex(plugId, out var displayIndex))
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (displayIndex < screens.Length)
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

    /// <summary>Binds an output to a plug (display or stream), replacing any earlier binding, and saves.</summary>
    public static void BindOutput(MachineConfig machineConfig, Guid outputId, Guid plugId)
    {
        if (TryGetDisplayIndex(plugId, out var displayIndex))
            BindOutputToDisplay(machineConfig, outputId, displayIndex);
        else
            BindOutputToStream(machineConfig, outputId, plugId);
    }

    /// <summary>Drops an output's binding and takes down its presentation window if it drove one.</summary>
    public static void UnbindOutput(MachineConfig machineConfig, Guid outputId)
    {
        var binding = machineConfig.TryGetBinding(outputId);
        machineConfig.Unbind(outputId);
        OutputSetupHandling.SaveActive();

        if (binding is { IsStream: false } && OutputManager.PresentedOutputId == outputId)
        {
            WindowManager.ShowSecondaryRenderWindow = false;
            OutputManager.PresentedOutputId = Guid.Empty;
        }
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
        var screens = System.Windows.Forms.Screen.AllScreens;
        machineConfig.Bind(new DeviceBinding
                               {
                                   OutputId = outputId,
                                   Kind = DeviceBinding.Kinds.Display,
                                   DisplayName = displayIndex < screens.Length ? screens[displayIndex].DeviceName : string.Empty,
                                   DisplayIndex = displayIndex,
                               });
        OutputSetupHandling.SaveActive();
        OutputManager.PresentedOutputId = outputId;
        WindowManager.ShowSecondaryRenderWindow = true;
        ProgramWindows.Viewer.SetFullScreen(displayIndex);
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
        machineConfig.Streams.Add(stream);
        OutputSetupHandling.SaveActive();
        return stream;
    }

    public static void RenameStream(MachineConfig machineConfig, Guid plugId, string newName)
    {
        var stream = machineConfig.FindStream(plugId);
        if (stream == null || stream.Name == newName)
            return;

        stream.Name = newName;
        OutputSetupHandling.SaveActive();
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
        if (machineConfig.FindStream(plugId) == null)
            return;

        machineConfig.Bind(new DeviceBinding
                               {
                                   OutputId = outputId,
                                   Kind = DeviceBinding.Kinds.Stream,
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
        foreach (var stream in machineConfig.Streams)
        {
            if (stream.Name == name)
                return true;
        }

        return false;
    }
    #endregion

    private static readonly List<string> _displayLabels = [];
}
