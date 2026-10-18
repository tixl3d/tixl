#nullable enable
using ImGuiNET;
using T3.Core.IO;
using T3.Core.Utils;
using T3.Editor.Gui.Dialog;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Midi;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Helpers;
using T3.IoServices.Midi;

namespace T3.Editor.Gui.Windows;

internal sealed partial class SettingsWindow
{
    private static void DrawMidiPanel(ref bool coreSettingsChanged)
    {
        // The usage scan walks all symbols, so it runs when the panel (re)appears, not every frame
        var frame = ImGui.GetFrameCount();
        if (frame - _midiPanelLastDrawnFrame > 1)
            RefreshMidiPanel();

        _midiPanelLastDrawnFrame = frame;

        FormInputs.AddVerticalSpace();
        if (CustomComponents.DrawCtaButton("Rescan Devices", Icon.Refresh, CustomComponents.ButtonStates.Default))
        {
            MidiConnectionManager.Rescan();
            CompatibleMidiDeviceHandling.InitializeConnectedDevices();
            RefreshMidiPanel();
        }

        CustomComponents.TooltipForLastItem("Reopens all MIDI devices and rescans the loaded projects for device names.");

        DrawAliasTable();
        DrawDeviceSpecificNames();
        coreSettingsChanged |= DrawCaptureLimit();
        FormInputs.SetIndentToParameters();

        if (_midiAliasDialog.Draw())
            RefreshMidiPanel();
    }

    private static void RefreshMidiPanel()
    {
        MidiDeviceCombo.RefreshConnectedDevices();
        MidiDeviceUsage.Refresh();
    }

    /// <summary>
    /// The saved alias entries, reorderable, followed by aliases that loaded projects reference but no entry maps yet.
    /// </summary>
    private static void DrawAliasTable()
    {
        var entries = MidiDeviceAliases.Entries;
        var changed = false;

        FormInputs.AddVerticalSpace();
        FormInputs.SetIndentToLeft();
        FormInputs.AddSectionSubHeader("Device Aliases");
        CustomComponents.HelpText($"""
                                   Projects store alias names like {MidiDeviceAlias.Prefix}ApcMini instead of device names.
                                   Map each alias to the device connected to this machine. Teaching a MidiInput stores the first alias mapped to its device.
                                   """);
        FormInputs.AddVerticalSpace(4);

        var rowHeight = FormInputs.ListRowHeight;
        var gripWidth = FormInputs.ListRowGripWidth;
        var drawList = ImGui.GetWindowDrawList();
        var removeIndex = -1;
        var moveFromIndex = -1;
        var moveToIndex = -1;

        ImGui.PushID("aliases");
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            ImGui.PushID(entry.Alias);

            var rowMin = ImGui.GetCursorScreenPos().Floor();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            var rowMax = rowMin + new Vector2(rowWidth, rowHeight).Floor();

            var isDragging = FormInputs.DrawRowDragGrip(index, entries.Count, rowHeight, ref moveFromIndex, ref moveToIndex);
            FormInputs.DrawListRowBackground(drawList, rowMin, rowMax, isDragging, drawGrip: true);

            MidiDeviceUsage.TryGetUsage(entry.Alias, out var usage);
            var usageCount = usage?.References.Count ?? 0;

            ImGui.SameLine(0, 0);
            DrawAliasRowContent(entry.Alias, usage, rowWidth - gripWidth, rowHeight);
            ComputeAliasColumns(rowWidth - gripWidth, rowHeight, out _, out var comboWidth);

            var productName = entry.ProductName ?? string.Empty;
            if (MidiDeviceCombo.Draw("##device", ref productName, comboWidth))
            {
                entry.ProductName = productName;
                changed = true;
            }

            ImGui.SameLine(0, 0);
            DrawUsageCount(usage, rowHeight);

            ImGui.SameLine(0, 0);
            DrawEditAliasButton(entry.Alias, rowHeight);

            ImGui.SameLine(0, 0);
            var canRemove = usageCount == 0;
            if (CustomComponents.IconButton(Icon.Close, new Vector2(rowHeight), canRemove ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled) && canRemove)
                removeIndex = index;

            CustomComponents.TooltipForLastItem(canRemove
                                                    ? "Remove alias"
                                                    : $"Used by {usageCount} inputs in loaded projects.",
                                                canRemove
                                                    ? null
                                                    : "Removing the mapping would leave those inputs without a device. Rename or merge it with the edit button, or change the inputs.");

            ImGui.PopID();
        }

        // Referenced but unmapped: picking a device creates the entry
        var aliasUsages = MidiDeviceUsage.AliasUsages;
        for (var index = 0; index < aliasUsages.Count; index++)
        {
            var usage = aliasUsages[index];
            if (HasEntry(entries, usage.DeviceName))
                continue;

            ImGui.PushID(usage.DeviceName);
            var rowMin = ImGui.GetCursorScreenPos().Floor();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            var rowMax = rowMin + new Vector2(rowWidth, rowHeight).Floor();
            FormInputs.DrawListRowBackground(drawList, rowMin, rowMax, isDragging: false, drawGrip: false);

            ImGui.Dummy(new Vector2(gripWidth, rowHeight));
            ImGui.SameLine(0, 0);
            DrawAliasRowContent(usage.DeviceName, usage, rowWidth - gripWidth, rowHeight);
            ComputeAliasColumns(rowWidth - gripWidth, rowHeight, out _, out var comboWidth);

            var productName = string.Empty;
            if (MidiDeviceCombo.Draw("##device", ref productName, comboWidth) && !string.IsNullOrEmpty(productName))
            {
                entries.Add(new MidiDeviceAlias { Alias = usage.DeviceName, ProductName = productName });
                changed = true;
            }

            ImGui.SameLine(0, 0);
            DrawUsageCount(usage, rowHeight);

            ImGui.SameLine(0, 0);
            DrawEditAliasButton(usage.DeviceName, rowHeight);

            // Nothing to remove: the row exists only because loaded projects reference the alias
            ImGui.SameLine(0, 0);
            ImGui.Dummy(new Vector2(rowHeight, rowHeight));
            ImGui.PopID();
        }

        ImGui.PopID();

        if (moveFromIndex >= 0)
        {
            (entries[moveFromIndex], entries[moveToIndex]) = (entries[moveToIndex], entries[moveFromIndex]);
            changed = true;
        }
        else if (removeIndex >= 0)
        {
            entries.RemoveAt(removeIndex);
            changed = true;
        }

        FormInputs.AddVerticalSpace(2);
        if (CustomComponents.DrawCtaButton("Add Alias...", Icon.Plus, CustomComponents.ButtonStates.Emphasized))
            _midiAliasDialog.ShowForNewAlias();

        CustomComponents.TooltipForLastItem($"Saved to {MidiDeviceAliases.FileName} in the settings folder. Player exports include a copy.");

        if (changed)
            MidiDeviceAliases.Save();
    }

    /// <summary>
    /// Splits the width left of the usage and remove columns between alias name and device combo, so the row
    /// still reads in a narrow settings window.
    /// </summary>
    private static void ComputeAliasColumns(float availableWidth, float rowHeight, out float nameWidth, out float comboWidth)
    {
        var scale = T3Ui.UiScaleFactor;
        // Usage column plus the edit and remove buttons are fixed
        var flexible = MathF.Max(1, availableWidth - UsageColumnWidth - 2 * rowHeight);
        comboWidth = (flexible * 0.55f).Clamp(100 * scale, 220 * scale);
        nameWidth = MathF.Max(1, flexible - comboWidth);
    }

    private static void DrawAliasRowContent(string alias, MidiDeviceUsage.DeviceUsage? usage, float availableWidth, float rowHeight)
    {
        ComputeAliasColumns(availableWidth, rowHeight, out var nameWidth, out _);
        ImGui.InvisibleButton("##alias", new Vector2(nameWidth, rowHeight));
        var textMin = ImGui.GetItemRectMin();
        var textMax = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        var textPos = new Vector2(textMin.X, textMin.Y + (rowHeight - ImGui.GetTextLineHeight()) / 2).Floor();
        drawList.PushClipRect(textMin, textMax, true);
        drawList.AddText(textPos, UiColors.TextMuted, MidiDeviceAlias.Prefix);
        textPos.X += ImGui.CalcTextSize(MidiDeviceAlias.Prefix).X;
        drawList.AddText(textPos, UiColors.Text, MidiDeviceAlias.WithoutPrefix(alias));
        drawList.PopClipRect();
        if (ImGui.IsItemHovered() && usage != null)
        {
            var isMapped = MidiDeviceAliases.TryFind(alias, out _);
            CustomComponents.TooltipForLastItem(isMapped ? alias : $"{alias} is referenced by loaded projects but not mapped on this machine.", usage.TooltipText);
        }

        ImGui.SameLine(0, 0);
    }

    private static void DrawEditAliasButton(string alias, float rowHeight)
    {
        // Placeholder glyph: the icon set has no edit/pen icon yet
        if (CustomComponents.IconButton(Icon.Settings, new Vector2(rowHeight)))
            _midiAliasDialog.ShowForAlias(alias);

        CustomComponents.TooltipForLastItem("Rename or merge alias...", "Renames the alias in all loaded projects. Choosing an existing alias merges the two.");
    }

    private static void DrawUsageCount(MidiDeviceUsage.DeviceUsage? usage, float rowHeight)
    {
        ImGui.InvisibleButton("##usage", new Vector2(UsageColumnWidth, rowHeight));
        var count = usage?.References.Count ?? 0;
        var textMin = ImGui.GetItemRectMin();
        var textPos = new Vector2(textMin.X + 6 * T3Ui.UiScaleFactor, textMin.Y + (rowHeight - ImGui.GetTextLineHeight()) / 2).Floor();
        ImGui.GetWindowDrawList().AddText(textPos, count == 0 ? UiColors.TextMuted.Fade(0.5f) : UiColors.TextMuted, count == 0 ? "unused" : $"used {count}x");
        if (ImGui.IsItemHovered() && usage != null)
            CustomComponents.TooltipForLastItem(usage.DeviceName, usage.TooltipText);
    }

    /// <summary>
    /// Physical device names found in the loaded projects. Each can be replaced by an alias in one step.
    /// </summary>
    private static void DrawDeviceSpecificNames()
    {
        FormInputs.AddVerticalSpace();
        FormInputs.SetIndentToLeft();
        FormInputs.AddSectionSubHeader("Device-Specific Names");
        CustomComponents.HelpText("""
                                  These inputs in the loaded projects name a physical device. Such names change between units,
                                  operating systems and USB ports. Replace them with an alias to keep the projects portable.
                                  """);
        FormInputs.AddVerticalSpace(4);

        var usages = MidiDeviceUsage.PhysicalNameUsages;
        if (usages.Count == 0)
        {
            CustomComponents.StylizedText("No device-specific names in the loaded projects.", Fonts.FontNormal, UiColors.TextMuted);
            return;
        }

        var rowHeight = FormInputs.ListRowHeight;
        var drawList = ImGui.GetWindowDrawList();
        const string buttonLabel = "Use Alias...";
        var buttonWidth = ImGui.CalcTextSize(buttonLabel).X + 2 * ImGui.GetStyle().FramePadding.X;

        ImGui.PushID("deviceNames");
        for (var index = 0; index < usages.Count; index++)
        {
            var usage = usages[index];
            ImGui.PushID(usage.DeviceName);

            var rowMin = ImGui.GetCursorScreenPos().Floor();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            var rowMax = rowMin + new Vector2(rowWidth, rowHeight).Floor();
            FormInputs.DrawListRowBackground(drawList, rowMin, rowMax, isDragging: false, drawGrip: false);

            var nameWidth = MathF.Max(1, rowWidth - UsageColumnWidth - buttonWidth);
            ImGui.InvisibleButton("##name", new Vector2(nameWidth, rowHeight));
            var textMin = ImGui.GetItemRectMin();
            var textMax = ImGui.GetItemRectMax();
            var textPos = new Vector2(textMin.X + 6 * T3Ui.UiScaleFactor, textMin.Y + (rowHeight - ImGui.GetTextLineHeight()) / 2).Floor();
            drawList.PushClipRect(textMin, textMax, true);
            drawList.AddText(textPos, MidiDeviceCombo.IsConnected(usage.DeviceName) ? UiColors.Text : UiColors.TextMuted, usage.DeviceName);
            drawList.PopClipRect();
            if (ImGui.IsItemHovered())
                CustomComponents.TooltipForLastItem(usage.DeviceName, usage.TooltipText);

            ImGui.SameLine(0, 0);
            DrawUsageCount(usage, rowHeight);

            ImGui.SameLine(0, 0);
            var canReplace = usage.RewritableCount > 0;
            ImGui.PushStyleColor(ImGuiCol.Text, canReplace ? UiColors.Text.Rgba : UiColors.TextMuted.Fade(0.5f).Rgba);
            if (ImGui.Button(buttonLabel, new Vector2(buttonWidth, rowHeight)) && canReplace)
                _midiAliasDialog.ShowForDeviceName(usage.DeviceName);

            ImGui.PopStyleColor();

            CustomComponents.TooltipForLastItem(canReplace
                                                    ? "Create an alias and replace this name in all loaded projects."
                                                    : "All usages are in read-only packages or connected inputs.");
            ImGui.PopID();
        }

        ImGui.PopID();
    }

    private static bool DrawCaptureLimit()
    {
        var changed = false;
        FormInputs.AddVerticalSpace();
        FormInputs.SetIndentToLeft();
        FormInputs.AddSectionSubHeader("Limit Captured Devices");
        CustomComponents.HelpText("Capturing a device blocks it for other applications. Enter one search string per line to capture matching devices only.");

        var limitMidiDevices = CoreSettings.Config.LimitMidiDeviceCapture ?? string.Empty;
        if (ImGui.InputTextMultiline("##Limit MidiDevices", ref limitMidiDevices, 2000, new Vector2(-1, 100 * T3Ui.UiScaleFactor)))
        {
            changed = true;
            CoreSettings.Config.LimitMidiDeviceCapture = string.IsNullOrEmpty(limitMidiDevices) ? null : limitMidiDevices;
            MidiConnectionManager.Rescan();
        }

        FormInputs.AddVerticalSpace();
        return changed;
    }

    private static bool HasEntry(List<MidiDeviceAlias> entries, string alias)
    {
        foreach (var entry in entries)
        {
            if (entry.Alias == alias)
                return true;
        }

        return false;
    }

    private static float UsageColumnWidth => 70 * T3Ui.UiScaleFactor;

    private static int _midiPanelLastDrawnFrame = -10;
    private static readonly MidiAliasDialog _midiAliasDialog = new();
}
