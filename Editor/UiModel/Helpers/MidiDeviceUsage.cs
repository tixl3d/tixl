#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Text;
using T3.IoServices.Midi;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui;
using T3.Editor.Gui.InputUi.SimpleInputUis;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;

namespace T3.Editor.UiModel.Helpers;

/// <summary>
/// Collects every MIDI device name stored in the loaded symbols, so the settings panel can list which
/// aliases are in use and replace device-specific names. The scan walks all symbols and runs on request only.
/// </summary>
internal static class MidiDeviceUsage
{
    internal sealed class Reference
    {
        internal Guid SymbolId;
        internal Guid ChildId;
        internal Guid InputId;
        internal string Path = string.Empty;
        internal bool IsReadOnly;
        internal bool IsConnected;
        internal bool IsRewritable => !IsReadOnly && !IsConnected;
    }

    internal sealed class DeviceUsage
    {
        internal string DeviceName = string.Empty;
        internal readonly List<Reference> References = new();
        internal int ReadOnlyCount;
        internal int ConnectedCount;
        internal int RewritableCount;
        internal string TooltipText = string.Empty;
    }

    /// <summary>Alias names referenced by loaded symbols, most used first.</summary>
    internal static IReadOnlyList<DeviceUsage> AliasUsages => _aliasUsages;

    /// <summary>Physical device names referenced by loaded symbols, most used first.</summary>
    internal static IReadOnlyList<DeviceUsage> PhysicalNameUsages => _physicalNameUsages;

    internal static bool TryGetUsage(string deviceName, [NotNullWhen(true)] out DeviceUsage? usage)
    {
        return _usagesByName.TryGetValue(deviceName, out usage);
    }

    internal static void Refresh()
    {
        _usagesByName.Clear();
        _aliasUsages.Clear();
        _physicalNameUsages.Clear();

        foreach (var symbol in SymbolRegistry.SymbolsFromAllPackages())
        {
            var isReadOnly = symbol.SymbolPackage.IsReadOnly;
            foreach (var child in symbol.Children.Values)
            {
                if (!SymbolUiRegistry.TryGetSymbolUi(child.Symbol.Id, out var childSymbolUi))
                    continue;

                foreach (var input in child.Inputs.Values)
                {
                    if (input.IsDefault)
                        continue;

                    if (input.Value is not InputValue<string> stringValue || string.IsNullOrEmpty(stringValue.Value))
                        continue;

                    if (!childSymbolUi.InputUis.TryGetValue(input.Id, out var inputUi)
                        || inputUi is not StringInputUi { Usage: StringInputUi.UsageType.MidiDevice })
                        continue;

                    var reference = new Reference
                                        {
                                            SymbolId = symbol.Id,
                                            ChildId = child.Id,
                                            InputId = input.Id,
                                            Path = $"{symbol.SymbolPackage.DisplayName} / {symbol.Name} / {child.ReadableName}",
                                            IsReadOnly = isReadOnly,
                                            IsConnected = IsInputConnected(symbol, child.Id, input.Id),
                                        };
                    AddReference(stringValue.Value, reference);
                }
            }
        }

        foreach (var usage in _usagesByName.Values)
        {
            usage.TooltipText = BuildTooltip(usage);
            if (MidiDeviceAlias.IsAlias(usage.DeviceName))
            {
                _aliasUsages.Add(usage);
            }
            else
            {
                _physicalNameUsages.Add(usage);
            }
        }

        _aliasUsages.Sort(CompareUsages);
        _physicalNameUsages.Sort(CompareUsages);
    }

    /// <summary>
    /// Replaces a device name in every rewritable reference as a single undo step and saves the touched symbols.
    /// Returns the number of inputs changed.
    /// </summary>
    internal static int ReplaceDeviceName(string fromName, string toName)
    {
        if (!_usagesByName.TryGetValue(fromName, out var usage))
            return 0;

        var macro = new MacroCommand($"Replace MIDI device '{fromName}' with '{toName}'");
        var count = 0;
        foreach (var reference in usage.References)
        {
            if (!reference.IsRewritable)
                continue;

            if (!SymbolRegistry.TryGetSymbol(reference.SymbolId, out var symbol)
                || !symbol.Children.TryGetValue(reference.ChildId, out var child)
                || !child.Inputs.TryGetValue(reference.InputId, out var input))
            {
                Log.Warning($"Skipping MIDI device reference that no longer exists: {reference.Path}");
                continue;
            }

            macro.AddAndExecCommand(new ChangeInputValueCommand(symbol, child.Id, input, new InputValue<string>(toName)));
            count++;
        }

        if (count == 0)
            return 0;

        UndoRedoStack.Add(macro);
        T3Ui.SaveInBackground(false);
        Refresh();
        return count;
    }

    private static void AddReference(string deviceName, Reference reference)
    {
        if (!_usagesByName.TryGetValue(deviceName, out var usage))
        {
            usage = new DeviceUsage { DeviceName = deviceName };
            _usagesByName[deviceName] = usage;
        }

        usage.References.Add(reference);
        if (reference.IsReadOnly)
        {
            usage.ReadOnlyCount++;
        }
        else if (reference.IsConnected)
        {
            usage.ConnectedCount++;
        }
        else
        {
            usage.RewritableCount++;
        }
    }

    private static bool IsInputConnected(Symbol symbol, Guid childId, Guid inputId)
    {
        foreach (var connection in symbol.Connections)
        {
            if (connection.TargetParentOrChildId == childId && connection.TargetSlotId == inputId)
                return true;
        }

        return false;
    }

    private static string BuildTooltip(DeviceUsage usage)
    {
        var sb = new StringBuilder();
        var shown = 0;
        foreach (var reference in usage.References)
        {
            if (shown++ >= MaxTooltipLines)
                break;

            sb.Append(reference.Path);
            if (reference.IsReadOnly)
                sb.Append("  (read-only)");
            else if (reference.IsConnected)
                sb.Append("  (connected input)");

            sb.Append('\n');
        }

        var remaining = usage.References.Count - MaxTooltipLines;
        if (remaining > 0)
            sb.Append($"... and {remaining} more");

        return sb.ToString().TrimEnd();
    }

    private static int CompareUsages(DeviceUsage a, DeviceUsage b)
    {
        var byCount = b.References.Count.CompareTo(a.References.Count);
        return byCount != 0 ? byCount : string.CompareOrdinal(a.DeviceName, b.DeviceName);
    }

    private const int MaxTooltipLines = 30;
    private static readonly Dictionary<string, DeviceUsage> _usagesByName = new();
    private static readonly List<DeviceUsage> _aliasUsages = new();
    private static readonly List<DeviceUsage> _physicalNameUsages = new();
}
