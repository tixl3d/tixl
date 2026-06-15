#nullable enable
using System.Text;
using ImGuiNET;
using ManagedBass;
using ManagedBass.Wasapi;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Audio;

/// <summary>
/// Shared WASAPI input-device picker used by the project settings and the global settings window.
/// A project stores an empty device name to mean "use this machine's default input"; the actual
/// machine-local device lives in <see cref="CoreSettings"/> so shared projects stay portable.
/// </summary>
internal static class AudioDeviceSelector
{
    internal const string DefaultDeviceLabel = "Default Audio Input";

    /// <summary>
    /// Project-level device combo. Offers the portable "Default Audio Input" entry (empty name)
    /// plus every concrete device as an explicit override. Returns true if <paramref name="projectDeviceName"/> changed.
    /// </summary>
    internal static bool DrawProjectDeviceCombo(string id, ref string projectDeviceName)
    {
        var changed = false;
        var label = string.IsNullOrEmpty(projectDeviceName) ? DefaultDeviceLabel : projectDeviceName;

        if (ImGui.BeginCombo(id, label, ImGuiComboFlags.HeightLarge))
        {
            if (ImGui.Selectable(DefaultDeviceLabel, string.IsNullOrEmpty(projectDeviceName), ImGuiSelectableFlags.NoAutoClosePopups))
            {
                projectDeviceName = string.Empty;
                changed = true;
            }

            changed |= DrawDeviceRows(ref projectDeviceName);
            ImGui.EndCombo();
        }

        if (changed)
            AudioEngine.OnAudioDeviceChanged();

        return changed;
    }

    /// <summary>
    /// Machine-local default device combo backed by <see cref="CoreSettings"/>. Returns true if changed.
    /// </summary>
    internal static bool DrawLocalDefaultDeviceCombo(string id)
    {
        var current = CoreSettings.Config.LocalAudioInputDeviceName ?? string.Empty;
        var label = string.IsNullOrEmpty(current) ? "(none selected)" : current;
        var changed = false;

        if (ImGui.BeginCombo(id, label, ImGuiComboFlags.HeightLarge))
        {
            changed |= DrawDeviceRows(ref current);
            ImGui.EndCombo();
        }

        if (changed)
        {
            CoreSettings.Config.LocalAudioInputDeviceName = current;
            CoreSettings.Save();
            AudioEngine.OnAudioDeviceChanged();
        }

        return changed;
    }

    private static bool DrawDeviceRows(ref string selectedName)
    {
        var changed = false;
        var deviceIndex = 0;
        foreach (var d in WasapiAudioInput.InputDevices)
        {
            // Seed the ID from the index: devices can share a display name, which ImGui flags as conflicting IDs.
            ImGui.PushID(deviceIndex++);
            var isSelected = d.DeviceInfo.Name == selectedName;
            if (ImGui.Selectable(d.DeviceInfo.Name, isSelected, ImGuiSelectableFlags.NoAutoClosePopups))
            {
                Bass.Configure(Configuration.UpdateThreads, false);
                selectedName = d.DeviceInfo.Name;
                changed = true;
            }

            if (ImGui.IsItemHovered())
                DrawDeviceTooltip(d.DeviceInfo);

            ImGui.PopID();
        }

        return changed;
    }

    private static void DrawDeviceTooltip(WasapiDeviceInfo di)
    {
        ImGui.BeginTooltip();
        ImGui.PushFont(Fonts.FontSmall);
        var sb = new StringBuilder();
        foreach (var f in typeof(WasapiDeviceInfo).GetProperties())
        {
            sb.Append(f.Name);
            sb.Append(": ");
            sb.Append(f.GetValue(di));
            sb.Append('\n');
        }

        ImGui.TextUnformatted(sb.ToString());
        ImGui.PopFont();
        ImGui.EndTooltip();
    }
}
