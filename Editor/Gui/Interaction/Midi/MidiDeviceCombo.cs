#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Styling;
using T3.IoServices.Midi;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Combo for picking one of the connected MIDI devices. A name that is no longer connected stays selected
/// and is flagged, so a mapping made at home survives until the controller is plugged in at the venue.
/// </summary>
internal static class MidiDeviceCombo
{
    internal const string NotSetLabel = "Not Set";

    internal static IReadOnlyList<string> ConnectedDeviceNames => _connectedDeviceNames;

    internal static void RefreshConnectedDevices()
    {
        _connectedDeviceNames.Clear();
        foreach (var info in MidiConnectionManager.MidiIns.Values)
        {
            if (!_connectedDeviceNames.Contains(info.ProductName))
                _connectedDeviceNames.Add(info.ProductName);
        }

        foreach (var info in MidiConnectionManager.MidiOutsWithDevices.Values)
        {
            if (!_connectedDeviceNames.Contains(info.ProductName))
                _connectedDeviceNames.Add(info.ProductName);
        }

        _connectedDeviceNames.Sort(string.CompareOrdinal);
    }

    internal static bool IsConnected(string productName)
    {
        return _connectedDeviceNames.Contains(productName);
    }

    /// <summary>Returns true when the selection changed. An empty <paramref name="productName"/> means not set.</summary>
    internal static bool Draw(string id, ref string productName, float width)
    {
        var isUnset = string.IsNullOrEmpty(productName);
        var isMissing = !isUnset && !IsConnected(productName);
        var label = isUnset
                        ? NotSetLabel
                        : isMissing
                            ? productName + " (NOT FOUND)"
                            : productName;

        var changed = false;
        ImGui.SetNextItemWidth(width);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Text, isUnset
                                                ? UiColors.TextMuted.Rgba
                                                : isMissing
                                                    ? UiColors.StatusWarning.Rgba
                                                    : UiColors.ForegroundFull.Rgba);
        if (ImGui.BeginCombo(id, label, ImGuiComboFlags.HeightLarge))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Rgba);
            if (ImGui.Selectable(NotSetLabel, isUnset))
            {
                productName = string.Empty;
                changed = true;
            }

            for (var index = 0; index < _connectedDeviceNames.Count; index++)
            {
                var name = _connectedDeviceNames[index];
                ImGui.PushID(index);
                if (ImGui.Selectable(name, name == productName))
                {
                    productName = name;
                    changed = true;
                }

                ImGui.PopID();
            }

            if (isMissing)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusWarning.Rgba);
                ImGui.Selectable(label, true);
                ImGui.PopStyleColor();
            }

            ImGui.PopStyleColor();
            ImGui.EndCombo();
        }

        ImGui.PopStyleColor(2);
        return changed;
    }

    private static readonly List<string> _connectedDeviceNames = new();
}
