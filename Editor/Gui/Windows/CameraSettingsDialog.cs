#nullable enable
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.ProjectHandling;
using T3.SystemUi;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// The camera settings window an operator opens through <see cref="CameraSettingsWindow"/> — TiXL's own take on
/// the Windows driver's "Video Capture Filter Properties" dialog, identical on every platform. Changes reach the
/// camera immediately; OK / Apply store them with the project, Cancel restores what the camera had when the
/// window opened. One window per operator instance.
/// </summary>
internal static class CameraSettingsDialog
{
    internal static void RegisterAsOpenHandler()
    {
        CameraSettingsWindow.OpenWindow = Open;
    }

    internal static void Draw()
    {
        for (var entryIndex = _entries.Count - 1; entryIndex >= 0; entryIndex--)
        {
            var entry = _entries[entryIndex];
            if (!DrawEntry(entry))
                _entries.RemoveAt(entryIndex);
        }
    }

    private sealed class Entry(Guid[] instancePath, int windowNumber)
    {
        public readonly Guid[] InstancePath = instancePath;
        public readonly int WindowNumber = windowNumber;
        public string Title = string.Empty;
        public string TitleDeviceName = "\0";
        public bool FocusRequested = true;
        public bool IsFirstFrame = true;

        /** What the camera had when its controls first showed up here — Cancel goes back to this. */
        public Dictionary<string, (int? Value, bool IsAuto)>? Original;

        /** Changes made in this window since it opened; shown instead of the read-back until the camera catches up. */
        public readonly Dictionary<string, (int Value, bool IsAuto)> Edits = new(StringComparer.Ordinal);

        public bool HasUncommittedEdits;

        /** The requested capture mode when the window opened (or at the last Apply) — Cancel goes back to it. */
        public CameraMode? OriginalFormat;

        /** Dropdown entries, rebuilt only when the camera's mode list or the selection changes. */
        public IReadOnlyList<CameraMode>? FormatOptionsSource;
        public readonly List<(int Width, int Height, string Label)> SizeOptions = [];
        public readonly Dictionary<(int Width, int Height), List<(int Fps, string Label)>> FpsOptionsBySize = [];
        public readonly List<(int Fps, string Label)> AllFpsOptions = [];
        public (CameraMode Requested, CameraMode Active) PreviewKey = (new CameraMode(-1, -1, -1), default);
        public string SizePreview = string.Empty;
        public string FpsPreview = string.Empty;
    }

    private static void Open(ICameraSettingsHolder holder)
    {
        if (holder is not Instance instance)
            return;

        var path = instance.InstancePath;
        foreach (var existing in _entries)
        {
            if (existing.InstancePath.SequenceEqual(path))
            {
                existing.FocusRequested = true;
                return;
            }
        }

        _entries.Add(new Entry([.. path], ++_windowCounter));
    }

    /** Returns false once the window was closed. */
    private static bool DrawEntry(Entry entry)
    {
        var instance = ProjectView.Focused?.Structure.GetInstanceFromIdPath(entry.InstancePath);
        var holder = instance as ICameraSettingsHolder;

        var deviceName = holder?.CameraSettingsDeviceName ?? string.Empty;
        if (!ReferenceEquals(deviceName, entry.TitleDeviceName))
        {
            entry.TitleDeviceName = deviceName;
            entry.Title = deviceName.Length > 0
                              ? $"{deviceName} Properties###CameraSettings{entry.WindowNumber}"
                              : $"Camera Properties###CameraSettings{entry.WindowNumber}";
        }

        if (entry.IsFirstFrame)
        {
            var size = new Vector2(520, 480) * T3Ui.UiScaleFactor;
            ImGui.SetNextWindowSize(size);
            ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter() - size * 0.5f);
            entry.IsFirstFrame = false;
        }

        if (entry.FocusRequested)
        {
            ImGui.SetNextWindowFocus();
            entry.FocusRequested = false;
        }

        var isOpen = true;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * T3Ui.UiScaleFactor);
        var isVisible = ImGui.Begin(entry.Title, ref isOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking);
        ImGui.PopStyleVar();

        if (isVisible)
        {
            if (holder == null || instance == null)
            {
                CustomComponents.StylizedText("The camera operator is no longer available.", Fonts.FontNormal, UiColors.TextMuted);
                if (ImGui.Button("Close"))
                    isOpen = false;
            }
            else
            {
                isOpen &= DrawContent(entry, holder, instance);
            }
        }

        ImGui.End();

        // Closing with the title bar's X keeps what is on the camera, like the driver dialog does.
        return isOpen;
    }

    /** Returns false when OK or Cancel closed the window. */
    private static bool DrawContent(Entry entry, ICameraSettingsHolder holder, Instance instance)
    {
        var settings = holder.GetCameraSettings();
        if (settings.Count > 0 && entry.Original == null)
            entry.Original = CaptureState(settings);

        DrawFormatRow(entry, holder, instance);

        var footerHeight = ImGui.GetFrameHeightWithSpacing() * 2 + 8 * T3Ui.UiScaleFactor;
        if (ImGui.BeginChild("##controls", new Vector2(0, -footerHeight)))
        {
            if (settings.Count == 0)
            {
                ImGui.Dummy(new Vector2(0, 8 * T3Ui.UiScaleFactor));
                var status = holder.CameraSettingsStatus;
                CustomComponents.StylizedText(status.Length > 0 ? status : "Reading camera controls...", Fonts.FontNormal, UiColors.TextMuted);
            }
            else if (ImGui.BeginTabBar("##groups"))
            {
                DrawGroupTab(entry, holder, settings, CameraSettingGroup.Image, "Video Proc Amp");
                DrawGroupTab(entry, holder, settings, CameraSettingGroup.Camera, "Camera Control");
                DrawGroupTab(entry, holder, settings, CameraSettingGroup.Other, "Other");
                ImGui.EndTabBar();
            }
        }

        ImGui.EndChild();

        return DrawFooter(entry, holder, instance, settings);
    }

    private static void DrawGroupTab(Entry entry, ICameraSettingsHolder holder, IReadOnlyList<CameraSetting> settings,
                                     CameraSettingGroup group, string title)
    {
        var hasAny = false;
        foreach (var setting in settings)
        {
            if (setting.Group == group)
            {
                hasAny = true;
                break;
            }
        }

        if (!hasAny || !ImGui.BeginTabItem(title))
            return;

        ImGui.Dummy(new Vector2(0, 4 * T3Ui.UiScaleFactor));

        var scale = T3Ui.UiScaleFactor;
        var autoColumnWidth = 44 * scale;
        var valueWidth = 72 * scale;
        var labelWidth = 150 * scale;
        var spacing = 8 * scale;

        // "Auto" column header, right-aligned over the checkboxes like the driver dialog.
        var headerX = ImGui.GetContentRegionAvail().X - autoColumnWidth;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + headerX);
        CustomComponents.StylizedText("Auto", Fonts.FontSmall, UiColors.TextMuted);

        foreach (var setting in settings)
        {
            if (setting.Group != group)
                continue;

            ImGui.PushID(setting.Name);
            DrawSettingRow(entry, holder, setting, labelWidth, valueWidth, autoColumnWidth, spacing);
            ImGui.PopID();
        }

        ImGui.EndTabItem();
    }

    private static void DrawSettingRow(Entry entry, ICameraSettingsHolder holder, CameraSetting setting,
                                       float labelWidth, float valueWidth, float autoColumnWidth, float spacing)
    {
        var rowStartX = ImGui.GetCursorPosX();
        var availableWidth = ImGui.GetContentRegionAvail().X;

        if (!setting.IsSupported)
        {
            DrawUnsupportedRow(setting, rowStartX, availableWidth, labelWidth, valueWidth, autoColumnWidth, spacing);
            return;
        }

        var hasEdit = entry.Edits.TryGetValue(setting.Name, out var edit);
        var isAuto = hasEdit ? edit.IsAuto : setting.IsAuto;
        var value = hasEdit ? edit.Value : setting.CurrentValue ?? setting.DefaultValue;

        // Label, right-aligned against the controls.
        var label = GetDisplayLabel(setting.Name);
        var labelSize = ImGui.CalcTextSize(label);
        ImGui.AlignTextToFramePadding();
        ImGui.SetCursorPosX(rowStartX + MathF.Max(labelWidth - labelSize.X, 0));
        var labelColor = setting.IsReadOnly ? UiColors.TextMuted : UiColors.Text;
        CustomComponents.StylizedText(label, Fonts.FontNormal, labelColor);

        ImGui.SameLine(rowStartX + labelWidth + spacing);
        var controlsWidth = availableWidth - labelWidth - spacing - autoColumnWidth;

        ImGui.BeginDisabled(setting.IsReadOnly || isAuto);
        var newValue = value;
        var changed = false;
        switch (setting.Kind)
        {
            case CameraSettingKind.Boolean:
            {
                var isOn = value != 0;
                if (ImGui.Checkbox("##value", ref isOn))
                {
                    newValue = isOn ? 1 : 0;
                    changed = true;
                }

                break;
            }

            case CameraSettingKind.Button:
            {
                if (ImGui.Button("Trigger"))
                {
                    holder.ApplyCameraSetting(setting.Name, setting.DefaultValue, null);
                }

                break;
            }

            case CameraSettingKind.Menu when setting.Items.Count > 0:
            {
                ImGui.SetNextItemWidth(controlsWidth);
                var preview = GetMenuLabel(setting, value);
                if (ImGui.BeginCombo("##value", preview))
                {
                    foreach (var item in setting.Items)
                    {
                        if (ImGui.Selectable(item.Label.Length > 0 ? item.Label : item.Value.ToString(), item.Value == value))
                        {
                            newValue = item.Value;
                            changed = true;
                        }
                    }

                    ImGui.EndCombo();
                }

                break;
            }

            default:
            {
                var sliderWidth = MathF.Max(controlsWidth - valueWidth - spacing, 40 * T3Ui.UiScaleFactor);
                ImGui.SetNextItemWidth(sliderWidth);
                var sliderValue = value;
                if (ImGui.SliderInt("##slider", ref sliderValue, setting.Minimum, setting.Maximum, string.Empty))
                {
                    newValue = SnapToStep(setting, sliderValue);
                    changed = newValue != value;
                }

                ImGui.SameLine(0, spacing);
                ImGui.SetNextItemWidth(valueWidth);
                var typedValue = value;
                if (ImGui.InputInt("##value", ref typedValue, 0, 0))
                {
                    newValue = SnapToStep(setting, typedValue);
                    changed = newValue != value;
                }

                break;
            }
        }

        ImGui.EndDisabled();

        if (changed)
        {
            holder.ApplyCameraSetting(setting.Name, newValue, null);
            entry.Edits[setting.Name] = (newValue, false);
            entry.HasUncommittedEdits = true;
        }

        // Auto column.
        ImGui.SameLine(rowStartX + availableWidth - autoColumnWidth + (autoColumnWidth - ImGui.GetFrameHeight()) * 0.5f);
        if (setting.SupportsAuto && !setting.IsReadOnly)
        {
            var autoValue = isAuto;
            if (ImGui.Checkbox("##auto", ref autoValue))
            {
                // Leaving auto keeps the value the camera had chosen, sent back as manual.
                holder.ApplyCameraSetting(setting.Name, autoValue ? null : value, autoValue ? true : null);
                entry.Edits[setting.Name] = (value, autoValue);
                entry.HasUncommittedEdits = true;
            }
        }
        else
        {
            ImGui.Dummy(new Vector2(ImGui.GetFrameHeight(), ImGui.GetFrameHeight()));
        }
    }

    /** A standard control this camera lacks: grayed out like the driver dialog, so the layout stays familiar. */
    private static void DrawUnsupportedRow(CameraSetting setting, float rowStartX, float availableWidth,
                                           float labelWidth, float valueWidth, float autoColumnWidth, float spacing)
    {
        var label = GetDisplayLabel(setting.Name);
        ImGui.AlignTextToFramePadding();
        ImGui.SetCursorPosX(rowStartX + MathF.Max(labelWidth - ImGui.CalcTextSize(label).X, 0));
        CustomComponents.StylizedText(label, Fonts.FontNormal, UiColors.TextDisabled);

        ImGui.BeginDisabled();
        ImGui.SameLine(rowStartX + labelWidth + spacing);
        var controlsWidth = availableWidth - labelWidth - spacing - autoColumnWidth;
        ImGui.SetNextItemWidth(MathF.Max(controlsWidth - valueWidth - spacing, 40 * T3Ui.UiScaleFactor));
        var placeholder = 0;
        ImGui.SliderInt("##slider", ref placeholder, 0, 1, string.Empty);
        ImGui.SameLine(0, spacing);
        ImGui.SetNextItemWidth(valueWidth);
        var emptyText = string.Empty;
        ImGui.InputText("##value", ref emptyText, 1, ImGuiInputTextFlags.ReadOnly);
        ImGui.SameLine(rowStartX + availableWidth - autoColumnWidth + (autoColumnWidth - ImGui.GetFrameHeight()) * 0.5f);
        var isAuto = false;
        ImGui.Checkbox("##auto", ref isAuto);
        ImGui.EndDisabled();

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("This camera does not offer this control.");
    }

    /** Resolution and frame rate, offered from the modes the camera reports. Picking one restarts the capture. */
    private static void DrawFormatRow(Entry entry, ICameraSettingsHolder holder, Instance instance)
    {
        var modes = holder.GetCameraModes();
        var requested = holder.RequestedCameraMode;
        entry.OriginalFormat ??= requested;
        UpdateFormatOptions(entry, modes, requested, holder.ActiveCameraMode);

        var scale = T3Ui.UiScaleFactor;
        var spacing = 8 * scale;
        var available = ImGui.GetContentRegionAvail().X;
        var resolutionLabelWidth = ImGui.CalcTextSize("Resolution").X;
        var fpsLabelWidth = ImGui.CalcTextSize("Frame Rate").X;
        var comboWidth = MathF.Max((available - resolutionLabelWidth - fpsLabelWidth - spacing * 4) * 0.5f, 60 * scale);

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("Resolution");
        ImGui.SameLine(0, spacing);
        ImGui.SetNextItemWidth(comboWidth);
        if (ImGui.BeginCombo("##resolution", entry.SizePreview))
        {
            if (ImGui.Selectable("Default", requested.Width <= 0))
                SetFormat(holder, instance, default);

            foreach (var (width, height, label) in entry.SizeOptions)
            {
                if (!ImGui.Selectable(label, requested.Width == width && requested.Height == height))
                    continue;

                // Keep the frame rate if the new size offers it, otherwise let the camera choose.
                var keepFps = entry.FpsOptionsBySize.TryGetValue((width, height), out var rates) && ContainsFps(rates, requested.Fps);
                SetFormat(holder, instance, new CameraMode(width, height, keepFps ? requested.Fps : 0));
            }

            if (entry.SizeOptions.Count == 0)
                CustomComponents.StylizedText("Reading modes...", Fonts.FontSmall, UiColors.TextMuted);

            ImGui.EndCombo();
        }

        ImGui.SameLine(0, spacing * 2);
        ImGui.TextUnformatted("Frame Rate");
        ImGui.SameLine(0, spacing);
        ImGui.SetNextItemWidth(comboWidth);
        if (ImGui.BeginCombo("##fps", entry.FpsPreview))
        {
            if (ImGui.Selectable("Auto", requested.Fps <= 0))
                SetFormat(holder, instance, requested with { Fps = 0 });

            var rates = requested.Width > 0 && entry.FpsOptionsBySize.TryGetValue((requested.Width, requested.Height), out var sizeRates)
                            ? sizeRates
                            : entry.AllFpsOptions;
            foreach (var (fps, label) in rates)
            {
                if (ImGui.Selectable(label, requested.Fps == fps))
                    SetFormat(holder, instance, requested with { Fps = fps });
            }

            ImGui.EndCombo();
        }

        ImGui.Dummy(new Vector2(0, 4 * scale));
    }

    private static void UpdateFormatOptions(Entry entry, IReadOnlyList<CameraMode> modes, CameraMode requested, CameraMode active)
    {
        if (!ReferenceEquals(modes, entry.FormatOptionsSource))
        {
            entry.FormatOptionsSource = modes;
            entry.SizeOptions.Clear();
            entry.FpsOptionsBySize.Clear();
            entry.AllFpsOptions.Clear();
            foreach (var mode in modes)
            {
                var size = (mode.Width, mode.Height);
                if (!entry.FpsOptionsBySize.TryGetValue(size, out var rates))
                {
                    rates = [];
                    entry.FpsOptionsBySize[size] = rates;
                    entry.SizeOptions.Add((mode.Width, mode.Height, $"{mode.Width} x {mode.Height}"));
                }

                if (mode.Fps > 0 && !ContainsFps(rates, mode.Fps))
                    rates.Add((mode.Fps, $"{mode.Fps} fps"));

                if (mode.Fps > 0 && !ContainsFps(entry.AllFpsOptions, mode.Fps))
                    entry.AllFpsOptions.Add((mode.Fps, $"{mode.Fps} fps"));
            }

            foreach (var rates in entry.FpsOptionsBySize.Values)
                rates.Sort(static (a, b) => b.Fps.CompareTo(a.Fps));

            entry.AllFpsOptions.Sort(static (a, b) => b.Fps.CompareTo(a.Fps));
        }

        if (entry.PreviewKey == (requested, active))
            return;

        entry.PreviewKey = (requested, active);
        var activeSize = active.Width > 0 ? $" ({active.Width} x {active.Height})" : string.Empty;
        entry.SizePreview = requested.Width > 0 ? $"{requested.Width} x {requested.Height}" : "Default" + activeSize;
        entry.FpsPreview = requested.Fps > 0 ? $"{requested.Fps} fps" : "Auto";
    }

    private static bool ContainsFps(List<(int Fps, string Label)> rates, int fps)
    {
        foreach (var (rate, _) in rates)
        {
            if (rate == fps)
                return true;
        }

        return false;
    }

    /** Writes the mode into the operator's format inputs as one undoable change. A zero size means the camera default. */
    private static void SetFormat(ICameraSettingsHolder holder, Instance instance, CameraMode mode)
    {
        var inputs = holder.FormatInputs;
        var parentSymbol = instance.SymbolChild.Parent;
        var childInputs = instance.SymbolChild.Inputs;
        if (parentSymbol == null
            || !childInputs.TryGetValue(inputs.ModeTypeInputId, out var modeTypeInput)
            || !childInputs.TryGetValue(inputs.ResolutionInputId, out var resolutionInput)
            || !childInputs.TryGetValue(inputs.FpsInputId, out var fpsInput))
        {
            Log.Warning("The camera format could not be changed: the operator has no parent or lacks its format inputs.");
            return;
        }

        var isDefault = mode.Width <= 0 && mode.Fps <= 0;
        var macro = new MacroCommand("Change Camera Format");
        macro.AddAndExecCommand(new ChangeInputValueCommand(parentSymbol, instance.SymbolChildId, modeTypeInput,
                                                            new InputValue<int>(isDefault ? inputs.DefaultModeType : inputs.CustomModeType),
                                                            instance));
        if (!isDefault)
        {
            macro.AddAndExecCommand(new ChangeInputValueCommand(parentSymbol, instance.SymbolChildId, resolutionInput,
                                                                new InputValue<Int2>(new Int2(Math.Max(mode.Width, 0), Math.Max(mode.Height, 0))),
                                                                instance));
            macro.AddAndExecCommand(new ChangeInputValueCommand(parentSymbol, instance.SymbolChildId, fpsInput,
                                                                new InputValue<int>(Math.Max(mode.Fps, 0)), instance));
        }

        UndoRedoStack.Add(macro);
    }

    /** Returns false when OK or Cancel closed the window. */
    private static bool DrawFooter(Entry entry, ICameraSettingsHolder holder, Instance instance, IReadOnlyList<CameraSetting> settings)
    {
        var status = holder.CameraSettingsStatus;
        if (settings.Count > 0 && status.Length > 0)
        {
            var isWarning = status.StartsWith("Warning", StringComparison.Ordinal);
            CustomComponents.StylizedText(status, Fonts.FontSmall, isWarning ? UiColors.StatusAttention : UiColors.TextMuted);
        }
        else
        {
            ImGui.NewLine();
        }

        var hasControls = settings.Count > 0;

        ImGui.BeginDisabled(!hasControls);
        if (ImGui.Button("Default"))
            ResetToDefaults(entry, holder, settings);
        ImGui.EndDisabled();

        var buttonWidth = 80 * T3Ui.UiScaleFactor;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        CustomComponents.RightAlign(buttonWidth * 3 + spacing * 2);

        var keepOpen = true;
        ImGui.BeginDisabled(!hasControls);
        if (ImGui.Button("OK", new Vector2(buttonWidth, 0)))
        {
            Commit(entry, holder, instance, settings);
            keepOpen = false;
        }

        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(buttonWidth, 0)))
        {
            Revert(entry, holder);
            if (entry.OriginalFormat is { } originalFormat && originalFormat != holder.RequestedCameraMode)
                SetFormat(holder, instance, originalFormat);

            keepOpen = false;
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(!entry.HasUncommittedEdits);
        if (ImGui.Button("Apply", new Vector2(buttonWidth, 0)))
            Commit(entry, holder, instance, settings);
        ImGui.EndDisabled();

        return keepOpen;
    }

    private static void ResetToDefaults(Entry entry, ICameraSettingsHolder holder, IReadOnlyList<CameraSetting> settings)
    {
        foreach (var setting in settings)
        {
            if (!IsStorable(setting))
                continue;

            holder.ApplyCameraSetting(setting.Name, setting.DefaultValue, null);
            entry.Edits[setting.Name] = (setting.DefaultValue, false);
        }

        entry.HasUncommittedEdits = true;
    }

    /** Stores the window's full state with the project as one undoable change. */
    private static void Commit(Entry entry, ICameraSettingsHolder holder, Instance instance, IReadOnlyList<CameraSetting> settings)
    {
        if (!entry.HasUncommittedEdits)
            return;

        var state = new Dictionary<string, (int Value, bool IsAuto)>(StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            if (!IsStorable(setting))
                continue;

            if (entry.Edits.TryGetValue(setting.Name, out var edit))
            {
                state[setting.Name] = edit;
            }
            else if (setting.CurrentValue is { } current)
            {
                state[setting.Name] = (current, setting.IsAuto);
            }
        }

        var parentSymbol = instance.SymbolChild.Parent;
        if (parentSymbol == null
            || !instance.SymbolChild.Inputs.TryGetValue(holder.StoredCameraSettingsInputId, out var input))
        {
            Log.Warning("Camera settings could not be stored: the operator has no parent or no settings input.");
            return;
        }

        var serialized = holder.SerializeCameraSettings(state);
        UndoRedoStack.AddAndExecute(new ChangeInputValueCommand(parentSymbol, instance.SymbolChildId, input,
                                                                new InputValue<string>(serialized), instance));
        entry.HasUncommittedEdits = false;

        // What is stored now is the new baseline for Cancel.
        entry.Original = null;
        entry.OriginalFormat = holder.RequestedCameraMode;
        entry.Edits.Clear();
    }

    private static void Revert(Entry entry, ICameraSettingsHolder holder)
    {
        if (entry.Original == null)
            return;

        foreach (var name in entry.Edits.Keys)
        {
            if (!entry.Original.TryGetValue(name, out var original))
                continue;

            if (original.IsAuto)
            {
                holder.ApplyCameraSetting(name, null, true);
            }
            else if (original.Value is { } value)
            {
                holder.ApplyCameraSetting(name, value, null);
            }
        }

        entry.Edits.Clear();
        entry.HasUncommittedEdits = false;
    }

    private static Dictionary<string, (int? Value, bool IsAuto)> CaptureState(IReadOnlyList<CameraSetting> settings)
    {
        var state = new Dictionary<string, (int? Value, bool IsAuto)>(StringComparer.Ordinal);
        foreach (var setting in settings)
            state[setting.Name] = (setting.CurrentValue, setting.IsAuto);

        return state;
    }

    private static bool IsStorable(CameraSetting setting)
        => setting.IsSupported && !setting.IsReadOnly && setting.Kind != CameraSettingKind.Button;

    private static int SnapToStep(CameraSetting setting, int value)
    {
        var step = Math.Max(setting.Step, 1);
        var snapped = setting.Minimum + (int)MathF.Round((value - setting.Minimum) / (float)step) * step;
        return Math.Clamp(snapped, setting.Minimum, setting.Maximum);
    }

    private static string GetMenuLabel(CameraSetting setting, int value)
    {
        foreach (var item in setting.Items)
        {
            if (item.Value == value)
                return item.Label.Length > 0 ? item.Label : item.Value.ToString();
        }

        return value.ToString();
    }

    /** "BacklightCompensation" → "Backlight Compensation", cached so drawing doesn't allocate. */
    private static string GetDisplayLabel(string name)
    {
        if (_displayLabels.TryGetValue(name, out var label))
            return label;

        var builder = new StringBuilder(name.Length + 4);
        for (var charIndex = 0; charIndex < name.Length; charIndex++)
        {
            var character = name[charIndex];
            if (charIndex > 0 && char.IsUpper(character) && char.IsLower(name[charIndex - 1]))
                builder.Append(' ');

            builder.Append(character);
        }

        label = builder.ToString();
        _displayLabels[name] = label;
        return label;
    }

    private static readonly List<Entry> _entries = [];
    private static readonly Dictionary<string, string> _displayLabels = new(StringComparer.Ordinal);
    private static int _windowCounter;
}
