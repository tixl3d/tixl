#nullable enable
using System.IO;
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Input;

/// <summary>
/// A set of custom widgets that allow to quickly draw dialogs with a more "traditional" labels on the left side of input fields.
/// It also provides a bunch of helper methods for minimal layout control. 
/// </summary>
internal static class FormInputs
{
    public static void BeginFrame()
    {
        SetIndentToParameters();
    }

    public static void AddSectionHeader(string label)
    {
        //AddVerticalSpace(1);
        ImGui.PushFont(Fonts.FontLarge);
        ImGui.Text(label);
        ImGui.PopFont();
        //AddVerticalSpace(20);
    }

    public static void AddSectionHeaderParam(string label)
    {
        ImGui.PushFont(Fonts.FontLarge);
        ImGui.Text(label.AddSpacesForImGuiOutput());
        ImGui.PopFont();
    }

    public static void AddSectionSubHeader(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.PushFont(Fonts.FontBold);

        AddVerticalSpace(15 * T3Ui.UiScaleFactor);
        ImGui.TextUnformatted(label);
        AddVerticalSpace(5 * T3Ui.UiScaleFactor);
        ImGui.PopFont();
        ImGui.PopStyleColor();
    }

    public static void DrawFieldSetHeader(string label, bool useParamIndent = false)
    {
        //    ImGui.SameLine(0,_paramIndent);


        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.PushFont(Fonts.FontSmall);

        AddVerticalSpace(5);

        if (useParamIndent)
            ImGui.SetCursorPosX(DefaultParameterIndent);

        ImGui.TextUnformatted(label.ToUpperInvariant());
        AddVerticalSpace(1);
        ImGui.PopFont();
        ImGui.PopStyleColor();
    }

    public static bool BeginGroup(string label)
    {
        AddVerticalSpace(5);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        // var isNotCollapsable = !label.EndsWith("...");
        // if (isNotCollapsable)
        // {
        //     ImGui.Text(label);
        //     ImGui.PopStyleColor();
        //     return true;
        // }

        // var id = ImGui.GetID(label);
        // if (isNotCollapsable && !_openedGroups.Contains(id))
        // {
        //     ImGui.SetNextItemOpen(true);
        //     _openedGroups.Add(id);
        // }

        var isOpen = ImGui.TreeNode(label);
        ImGui.PopStyleColor();
        if (isOpen)
            ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 0);

        return isOpen;
    }

    // private static HashSet<uint> _openedGroups = new();

    public static void EndGroup()
    {
        ImGui.PopStyleVar();
        ImGui.TreePop();
    }

    public static bool AddInt(string label,
        ref int value,
        int min = int.MinValue,
        int max = int.MaxValue,
        float scale = 1,
        string? tooltip = null,
        int defaultValue = NotADefaultValue)
    {
        var hasReset = defaultValue != NotADefaultValue;
        var isDefault = hasReset && value == defaultValue;

        // Fade the row while it holds its default value, so an unchanged field reads as inactive (matches AddFloat).
        if (isDefault)
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, DefaultFadeAlpha * ImGui.GetStyle().Alpha);

        DrawInputLabel(label);

        ImGui.PushID(label);

        var size = GetAvailableInputSize(tooltip, hasReset, fillWidth: true, maxWidth: MaxNumberInputWidth);
        var result = SingleValueEdit.Draw(ref value, size, min, max, true, true, scale);
        ImGui.PopID();

        AppendTooltip(tooltip);
        if (AppendResetButton(hasReset && !isDefault, label))
        {
            value = defaultValue;
            result |= InputEditStateFlags.ModifiedAndFinished;
        }

        if (isDefault)
            ImGui.PopStyleVar();

        var modified = (result & InputEditStateFlags.Modified) != InputEditStateFlags.Nothing;
        return modified;
    }

    private const float DefaultFadeAlpha = 0.7f;

    // Number fields fill the row on narrow panels but cap here so they don't stretch across a wide settings
    // panel; the value-range indicator stays readable and float/int rows line up. (Unscaled px.)
    internal const float MaxNumberInputWidth = 280f;

    public static bool AddFloat(string label,
        ref float value,
        float min = float.NegativeInfinity,
        float max = float.PositiveInfinity,
        float scale = 0.01f,
        bool clampMin = false,
        bool clampMax = false,
        string? tooltip = null,
        float defaultValue = float.NaN)
    {
        var result = AddFloatWithEditState(label, ref value, min, max, scale, clampMin, clampMax, tooltip, defaultValue);
        return (result & InputEditStateFlags.Modified) != InputEditStateFlags.Nothing;
    }

    /// <summary>
    /// Like <see cref="AddFloat"/> but returns the full edit state, so callers can tell a drag in progress
    /// (<see cref="InputEditStateFlags.Modified"/>) from its completion (<see cref="InputEditStateFlags.Finished"/>).
    /// </summary>
    public static InputEditStateFlags AddFloatWithEditState(string label,
        ref float value,
        float min = float.NegativeInfinity,
        float max = float.PositiveInfinity,
        float scale = 0.01f,
        bool clampMin = false,
        bool clampMax = false,
        string? tooltip = null,
        float defaultValue = float.NaN)
    {
        var hasReset = !float.IsNaN(defaultValue);
        var isDefault = hasReset && Math.Abs(value - defaultValue) < 0.0001f;
        if (isDefault)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, DefaultFadeAlpha * ImGui.GetStyle().Alpha);
        }

        DrawInputLabel(label);

        var size = GetAvailableInputSize(tooltip, hasReset, fillWidth: true, maxWidth: MaxNumberInputWidth);

        ImGui.PushID(label);

        var result = SingleValueEdit.Draw(ref value, size, min, max, clampMin, clampMax, scale);
        ImGui.PopID();

        AppendTooltip(tooltip);
        if (AppendResetButton(hasReset && !isDefault, label))
        {
            value = defaultValue;
            result |= InputEditStateFlags.ModifiedAndFinished;
        }

        if (isDefault)
        {
            ImGui.PopStyleVar();
        }

        return result;
    }

    /// <param name="itemLabel">Optional display-name formatter for each enum value (e.g. "HapQ" → "Hap Q").
    /// When null, the raw enum member names are shown.</param>
    /// <param name="itemSuffix">Optional secondary text drawn muted after each item's label (e.g. a size/time
    /// estimate). When provided, the dropdown renders items itself so the suffix can be de-emphasised.</param>
    public static bool AddEnumDropdown<T>(ref T selectedValue, string? label, string? tooltip = null,
        T defaultValue = default, Func<T, string>? itemLabel = null, Func<T, string>? itemSuffix = null)
        where T : struct, Enum, IConvertible, IFormattable
    {
        DrawInputLabel(label);

        var inputSize = GetAvailableInputSize(tooltip, false, true);
        ImGui.SetNextItemWidth(inputSize.X);

        var modified = DrawEnumDropdown(ref selectedValue, label, defaultValue, itemLabel, itemSuffix);

        AppendTooltip(tooltip);

        return modified;
    }

    public static bool DrawEnumDropdown<T>(ref T selectedValue, string? label, T defaultValue = default,
                                           Func<T, string>? itemLabel = null, Func<T, string>? itemSuffix = null)
        where T : struct, Enum, IConvertible, IFormattable, IComparable
    {
        var values = Enum.GetValues<T>();
        var names = Enum.GetNames<T>();
        if (itemLabel != null)
        {
            for (var i = 0; i < values.Length; i++)
                names[i] = itemLabel(values[i]);
        }

        var selectedIndex = 0;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i].Equals(selectedValue))
            {
                selectedIndex = i;
                break;
            }
        }

        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Text,
            selectedValue.Equals(defaultValue) ? UiColors.TextMuted.Rgba : UiColors.ForegroundFull.Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);

        bool modified;
        if (itemSuffix != null)
            modified = DrawEnumComboWithMutedSuffix(ref selectedValue, label, values, names, selectedIndex, itemSuffix);
        else
        {
            modified = ImGui.Combo($"##dropDown{typeof(T)}{label}", ref selectedIndex, names, names.Length, names.Length);
            if (modified)
                selectedValue = values[selectedIndex];
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);

        return modified;
    }

    // Custom combo so the per-item suffix (e.g. "→ ~95 MB, ~34s") can render in TextMuted while the label keeps the
    // normal colour. The closed preview shows label + suffix in one colour (BeginCombo can't two-tone it); the open
    // list two-tones each row via a full-width hidden Selectable with the label/suffix drawn over it.
    private static bool DrawEnumComboWithMutedSuffix<T>(ref T selectedValue, string? label, T[] values, string[] names,
                                                        int selectedIndex, Func<T, string> itemSuffix)
        where T : struct, Enum
    {
        var preview = names[selectedIndex] + itemSuffix(values[selectedIndex]);
        var modified = false;
        if (ImGui.BeginCombo($"##dropDown{typeof(T)}{label}", preview))
        {
            for (var i = 0; i < values.Length; i++)
            {
                var startX = ImGui.GetCursorPosX();
                if (ImGui.Selectable($"##enumItem{i}", i == selectedIndex))
                {
                    selectedValue = values[i];
                    modified = true;
                }

                ImGui.SameLine(startX);
                ImGui.TextUnformatted(names[i]);

                var suffix = itemSuffix(values[i]);
                if (!string.IsNullOrEmpty(suffix))
                {
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                    ImGui.TextUnformatted(suffix);
                    ImGui.PopStyleColor();
                }
            }

            ImGui.EndCombo();
        }

        return modified;
    }


    public static bool AddDropdown(ref string selectedValue, IEnumerable<string?> values, string label,
        string? tooltip = null)
    {
        DrawInputLabel(label);
        var spaceForTooltip = 0f;
        if (tooltip != null)
        {
            spaceForTooltip = 30f;
        }

        var inputSize = GetAvailableInputSize(null, false, true, spaceForTooltip);
        ImGui.SetNextItemWidth(inputSize.X);

        var modified = false;
        if (ImGui.BeginCombo("##SelectTheme",
                selectedValue,
                ImGuiComboFlags.HeightLarge))
        {
            foreach (var value in values)
            {
                if (value == null)
                    continue;

                var isSelected = value == selectedValue;
                if (!ImGui.Selectable($"{value}",
                        isSelected,
                        ImGuiSelectableFlags.NoAutoClosePopups))
                    continue;

                ImGui.CloseCurrentPopup();
                selectedValue = value;
                modified = true;
            }

            ImGui.EndCombo();
        }

        AppendTooltip(tooltip);
        return modified;
    }

    public static bool AddDropdown<T>(ref T selectedValue,
        IEnumerable<T> values,
        string label,
        Func<T, string> getDisplayTextFunc,
        string? tooltip = null)
    {
        DrawInputLabel(label);

        const string imguiLabelFmt = "##Select{0}{1}";
        var imguiLabel = string.Format(imguiLabelFmt, label, nameof(T));

        // const string defaultDisplayTextFmt = "Select {0}";
        // defaultDisplayText ??= string.Format(defaultDisplayTextFmt, selectedValue);

        var previewLabel = selectedValue == null ? "please select" : getDisplayTextFunc(selectedValue);

        // Constrain to the standard input column (reserving room for the tooltip icon) like the other inputs —
        // otherwise the combo spans the whole content region and overflows narrow panels.
        var inputSize = GetAvailableInputSize(tooltip, false, true);
        ImGui.SetNextItemWidth(inputSize.X);

        var modified = false;
        if (ImGui.BeginCombo(imguiLabel,
                previewLabel,
                ImGuiComboFlags.HeightLarge))
        {
            foreach (var value in values)
            {
                if (value == null)
                    continue;

                var equalityComparer = EqualityComparer<T>.Default;
                var isSelected = equalityComparer.Equals(value, selectedValue);
                // if (!ImGui.Selectable($"{value}", isSelected, ImGuiSelectableFlags.NoAutoClosePopups))
                //     continue;

                if (!ImGui.Selectable(getDisplayTextFunc(value), isSelected, ImGuiSelectableFlags.NoAutoClosePopups))
                    continue;

                ImGui.CloseCurrentPopup();
                selectedValue = value;
                modified = true;
            }

            ImGui.EndCombo();
        }

        AppendTooltip(tooltip);
        return modified;
    }

    /// <summary>
    /// Full-width list of folders. Rows are reordered by dragging their grip, can be opened in Explorer
    /// or removed, and "Add..." appends a folder from the system picker. Missing folders are flagged.
    /// </summary>
    public static bool AddFolderList(string label, IList<string> folders, string? tooltip = null)
    {
        var scale = T3Ui.UiScaleFactor;
        var rowHeight = ListRowHeight;
        var gripWidth = ListRowGripWidth;
        var drawList = ImGui.GetWindowDrawList();

        ImGui.PushID(label);
        DrawFolderListHeader(label, tooltip);

        var modified = false;
        var removeIndex = -1;
        var moveFromIndex = -1;
        var moveToIndex = -1;

        for (var index = 0; index < folders.Count; index++)
        {
            var folder = folders[index];
            // Keyed by path, not index, so the active grip stays with its row while the list reorders.
            ImGui.PushID(folder);

            var rowMin = ImGui.GetCursorScreenPos().Floor();
            var rowWidth = ImGui.GetContentRegionAvail().X;
            var rowMax = rowMin + new Vector2(rowWidth, rowHeight).Floor();

            var isDragging = DrawRowDragGrip(index, folders.Count, rowHeight, ref moveFromIndex, ref moveToIndex);
            DrawListRowBackground(drawList, rowMin, rowMax, isDragging, drawGrip: true);

            // Path text, clipped before the two tool icons
            var buttonSize = new Vector2(rowHeight);
            ImGui.SameLine(0, 0);
            var textWidth = MathF.Max(1, rowWidth - gripWidth - 2 * buttonSize.X);
            ImGui.InvisibleButton("##path", new Vector2(textWidth, rowHeight));
            var exists = Directory.Exists(folder);
            var textMin = ImGui.GetItemRectMin();
            var textMax = ImGui.GetItemRectMax();
            var textPos = new Vector2(textMin.X, textMin.Y + (rowHeight - ImGui.GetTextLineHeight()) / 2).Floor();
            drawList.PushClipRect(textMin, textMax, true);
            drawList.AddText(textPos, exists ? UiColors.Text : UiColors.StatusWarning, folder);
            drawList.PopClipRect();
            if (ImGui.IsItemHovered())
                CustomComponents.TooltipForLastItem(exists ? folder : "Folder does not exist", exists ? null : folder);

            ImGui.SameLine(0, 0);
            if (CustomComponents.IconButton(Icon.Folder, buttonSize, exists ? CustomComponents.ButtonStates.Default : CustomComponents.ButtonStates.Disabled) && exists)
                CoreUi.Instance.OpenWithDefaultApplication(folder);

            CustomComponents.TooltipForLastItem("Open in Explorer");

            ImGui.SameLine(0, 0);
            if (CustomComponents.AttentionIconButton(Icon.Close, buttonSize))
                removeIndex = index;

            CustomComponents.TooltipForLastItem("Remove from list");

            ImGui.PopID();
        }

        // List changes are applied after drawing so every row of this frame keeps a stable index.
        if (moveFromIndex >= 0)
        {
            (folders[moveFromIndex], folders[moveToIndex]) = (folders[moveToIndex], folders[moveFromIndex]);
            modified = true;
        }
        else if (removeIndex >= 0)
        {
            folders.RemoveAt(removeIndex);
            modified = true;
        }

        AddVerticalSpace(2);
        if (CustomComponents.DrawCtaButton("Add...", Icon.None, CustomComponents.ButtonStates.Emphasized))
        {
            var pickedFolder = FileOperations.TryPickAbsoluteFolder();
            if (!string.IsNullOrEmpty(pickedFolder) && !ContainsFolder(folders, pickedFolder))
            {
                folders.Add(pickedFolder);
                modified = true;
            }
        }

        CustomComponents.TooltipForLastItem("Choose a folder to add to the list.");
        ImGui.PopID();
        return modified;
    }

    /// <summary>Height of a row in reorderable settings lists, in scaled pixels.</summary>
    public static float ListRowHeight => MathF.Round(FolderRowHeight * T3Ui.UiScaleFactor);

    /// <summary>Width of the drag grip at the start of a reorderable row, in scaled pixels.</summary>
    public static float ListRowGripWidth => Icons.FontSize + 8 * T3Ui.UiScaleFactor;

    /// <summary>
    /// The invisible grip at the start of a reorderable row. Dragging it past half a row requests a swap
    /// with the neighbour through <paramref name="moveFromIndex"/> / <paramref name="moveToIndex"/>, which the
    /// caller applies after drawing all rows. Returns true while the grip is dragged.
    /// </summary>
    public static bool DrawRowDragGrip(int index, int rowCount, float rowHeight, ref int moveFromIndex, ref int moveToIndex)
    {
        ImGui.InvisibleButton("##grip", new Vector2(ListRowGripWidth, rowHeight));
        var isDragging = ImGui.IsItemActive();
        if (ImGui.IsItemHovered() || isDragging)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);

        if (!isDragging)
            return false;

        var rowStride = rowHeight + ImGui.GetStyle().ItemSpacing.Y;
        var dragY = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y;
        if (dragY < -rowStride * 0.5f && index > 0)
        {
            moveFromIndex = index;
            moveToIndex = index - 1;
            ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
        }
        else if (dragY > rowStride * 0.5f && index < rowCount - 1)
        {
            moveFromIndex = index;
            moveToIndex = index + 1;
            ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
        }

        return true;
    }

    /// <summary>Rounded row background for reorderable lists, with the grip icon when the row can be dragged.</summary>
    public static void DrawListRowBackground(ImDrawListPtr drawList, Vector2 rowMin, Vector2 rowMax, bool isDragging, bool drawGrip)
    {
        var scale = T3Ui.UiScaleFactor;
        var backgroundColor = isDragging ? UiColors.BackgroundActive.Fade(0.4f) : UiColors.BackgroundButton;
        drawList.AddRectFilled(rowMin, rowMax, backgroundColor, 4 * scale);

        if (!drawGrip)
            return;

        var rowHeight = rowMax.Y - rowMin.Y;
        var gripColor = isDragging ? UiColors.Text : UiColors.TextMuted.Fade(0.5f);
        var gripPos = new Vector2(rowMin.X + 4 * scale, rowMin.Y + (rowHeight - Icons.FontSize) / 2).Floor();
        Icons.DrawIconAtScreenPosition(Icon.DragIndicator, gripPos, drawList, gripColor);
    }

    private static void DrawFolderListHeader(string label, string? tooltip)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.PushFont(Fonts.FontBold);
        AddVerticalSpace(15);
        ImGui.TextUnformatted(label);
        ImGui.PopFont();
        ImGui.PopStyleColor();

        if (!string.IsNullOrEmpty(tooltip))
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - Icons.FontSize);
            DrawHelpMarker(tooltip);
        }

        AddVerticalSpace(2);
    }

    private static bool ContainsFolder(IList<string> folders, string folder)
    {
        var normalized = Path.TrimEndingDirectorySeparator(folder);
        foreach (var existing in folders)
        {
            if (string.Equals(Path.TrimEndingDirectorySeparator(existing), normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private const float FolderRowHeight = 26;

    public static bool AddSegmentedButtonWithLabel<T>(ref T selectedValue, string label, string? tooltip = null)
        where T : struct, Enum
    {
        DrawInputLabel(label);
        var modified = CustomComponents.SegmentedButton(ref selectedValue);
        AppendTooltip(tooltip);
        return modified;
    }

    private const string NoDefaultString = "_";

    /// <summary>
    /// Draws string input or file picker. 
    /// </summary>
    public static bool AddStringInput(string label,
        ref string value,
        string? placeHolder = null,
        string? warning = null,
        string? tooltip = null,
        string? defaultValue = NoDefaultString,
        bool autoFocus = false)
    {
        if (string.IsNullOrEmpty(label))
        {
            Log.Error("AddStringInput() requires an id to work. Use ## prefix to hide.");
            label = "##fallback";
        }

        var hasDefault = defaultValue != NoDefaultString;
        var isDefault = hasDefault && value == defaultValue;

        if (isDefault)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, DefaultFadeAlpha * ImGui.GetStyle().Alpha);
        }

        DrawInputLabel(label);
        var wasNull = value == null!; // Support legacy calls
        if (wasNull)
            value = string.Empty;

        var inputSize = GetAvailableInputSize(tooltip, false, true);
        ImGui.SetNextItemWidth(inputSize.X);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);


        var modified = ImGui.InputText("##" + label, ref value, 1000);
        if (!modified && wasNull)
            value = null!; // Support legacy calls 

        if (autoFocus)
        {
            // Todo - how the hell do you make this not select the entire text?
            ImGui.SetKeyboardFocusHere(-1);
        }

        ImGui.PopStyleVar();

        if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(placeHolder))
        {
            var drawList = ImGui.GetWindowDrawList();
            var minPos = ImGui.GetItemRectMin();
            var maxPos = ImGui.GetItemRectMax();
            drawList.PushClipRect(minPos, maxPos);
            drawList.AddText(minPos + new Vector2(8, 3) * T3Ui.UiScaleFactor, UiColors.ForegroundFull.Fade(0.25f),
                placeHolder);
            drawList.PopClipRect();
        }

        AppendTooltip(tooltip);
        if (isDefault)
        {
            ImGui.PopStyleVar();
        }

        if (AppendResetButton(hasDefault && !isDefault, label))
        {
            value = defaultValue ?? string.Empty;
            modified = true;
        }

        DrawWarningBelowField(warning);

        return modified;
    }

    public static bool AddStringInputWithSuggestions(string label,
        ref string value,
        IOrderedEnumerable<string> items,
        string? placeHolder = null,
        string? warning = null,
        string? tooltip = null,
        string? defaultValue = NoDefaultString,
        bool autoFocus = false)
    {
        if (string.IsNullOrEmpty(label))
        {
            Log.Error("AddStringInput() requires an id to work. Use ## prefix to hide.");
            label = "##fallback";
        }

        defaultValue ??= NoDefaultString;

        var hasDefault = defaultValue != NoDefaultString;
        var isDefault = hasDefault && value == defaultValue;

        if (isDefault)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, DefaultFadeAlpha * ImGui.GetStyle().Alpha);
        }

        DrawInputLabel(label);

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        var wasNull = value == null;
        if (wasNull)
            value = string.Empty;

        var inputSize = GetAvailableInputSize(tooltip, false, true);
        ImGui.SetNextItemWidth(inputSize.X);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);

        var modified = InputWithTypeAheadSearch.Draw("##typeAheadSearch",
            items,
            !string.IsNullOrEmpty(warning),
            ref value!, out _, out var _);

        if (!modified && wasNull)
            value = null!;

        if (autoFocus)
        {
            // Todo - how the hell do you make this not select the entire text?
            ImGui.SetKeyboardFocusHere(-1);
        }

        ImGui.PopStyleVar();

        if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(placeHolder))
        {
            var drawList = ImGui.GetWindowDrawList();
            var minPos = ImGui.GetItemRectMin();
            var maxPos = ImGui.GetItemRectMax();
            drawList.PushClipRect(minPos, maxPos);
            drawList.AddText(minPos + new Vector2(8, 3) * T3Ui.UiScaleFactor, UiColors.ForegroundFull.Fade(0.25f),
                placeHolder);
            drawList.PopClipRect();
        }

        AppendTooltip(tooltip);
        if (isDefault)
        {
            ImGui.PopStyleVar();
        }

        if (AppendResetButton(hasDefault && !isDefault, label))
        {
            value = defaultValue;
            modified = true;
        }

        DrawWarningBelowField(warning);

        return modified;
    }


    /// <summary>
    /// Draws string input or file picker. 
    /// </summary>
    public static bool AddFilePicker(string label,
        ref string? value,
        string? placeHolder = null,
        string? warning = null,
        string? tooltip = null,
        FileOperations.FilePickerTypes showFilePicker = FileOperations.FilePickerTypes.None)
    {
        DrawInputLabel(label);

        var isFilePickerVisible = showFilePicker != FileOperations.FilePickerTypes.None;
        float spaceForFilePicker = isFilePickerVisible ? 80 * T3Ui.UiScaleFactor : 0;
        var inputSize = GetAvailableInputSize(null, false, true, spaceForFilePicker);
        ImGui.SetNextItemWidth(inputSize.X);

        var wasNull = value == null;
        if (wasNull)
            value = string.Empty;

        var modified = ImGui.InputText("##" + label, ref value, 1000);
        if (!modified && wasNull)
            value = null;

        if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(placeHolder))
        {
            var drawList = ImGui.GetWindowDrawList();
            var minPos = ImGui.GetItemRectMin();
            var maxPos = ImGui.GetItemRectMax();
            drawList.PushClipRect(minPos, maxPos);
            drawList.AddText(minPos + new Vector2(8, 3), UiColors.ForegroundFull.Fade(0.25f), placeHolder);
            drawList.PopClipRect();
        }

        if (isFilePickerVisible)
        {
            ImGui.PushID(label);
            modified |= FileOperations.DrawFileSelector(showFilePicker, ref value);
            ImGui.PopID();
        }

        AppendTooltip(tooltip);


        DrawWarningBelowField(warning);
        return modified;
    }

    public static bool AddCheckBox(string label,
        ref bool value,
        string? tooltip = null,
        bool? defaultValue = null)
    {
        var hasDefault = defaultValue != null;
        var isDefault = defaultValue != null && value == (bool) defaultValue;

        if (isDefault)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, DefaultFadeAlpha * ImGui.GetStyle().Alpha);
        }

        ImGui.SetCursorPosX(MathF.Max(LeftParameterPadding, 0) + 20 * T3Ui.UiScaleFactor);
        var modified = DrawCheckbox(label, ref value);

        AppendTooltip(tooltip);
        if (isDefault)
        {
            ImGui.PopStyleVar();
        }

        if (AppendResetButton(hasDefault && !isDefault, label))
        {
            value = defaultValue ?? false;
            modified = true;
        }

        return modified;
    }

    /// <summary>
    /// Rounded checkbox with a white atlas checkmark when on — replaces ImGui.Checkbox so checkboxes
    /// match the editor's look (rounded field, baked glyph) instead of the default tick.
    /// </summary>
    private static bool DrawCheckbox(string label, ref bool value)
    {
        var scale = T3Ui.UiScaleFactor;
        var h = ImGui.GetFrameHeight();
        var boxSize = MathF.Round(h * 0.8f);

        var hasLabel = !string.IsNullOrEmpty(label) && !label.StartsWith("##");
        var labelSize = hasLabel ? ImGui.CalcTextSize(label) : Vector2.Zero;
        var spacing = hasLabel ? 8 * scale : 0;

        var id = string.IsNullOrEmpty(label) ? "##checkbox" : label;
        var clicked = ImGui.InvisibleButton(id, new Vector2(boxSize + spacing + labelSize.X, h));
        if (clicked)
            value = !value;

        var min = ImGui.GetItemRectMin();
        var isHovered = ImGui.IsItemHovered();
        var dl = ImGui.GetWindowDrawList();

        var boxMin = new Vector2(min.X, min.Y + (h - boxSize) * 0.5f);
        var boxMax = boxMin + new Vector2(boxSize, boxSize);
        var boxColor = isHovered ? UiColors.BackgroundInputFieldHover : UiColors.BackgroundInputField;
        dl.AddRectFilled(boxMin, boxMax, boxColor, 4 * scale);

        if (value)
        {
            var iconSize = boxSize * 0.9f;
            var iconPos = boxMin + new Vector2((boxSize - iconSize) * 0.5f);
            Icons.DrawIconAtScreenPosition(Icon.Checkmark, iconPos, new Vector2(iconSize), dl, UiColors.ForegroundFull);
        }

        if (hasLabel)
        {
            var textPos = new Vector2(boxMax.X + spacing, min.Y + (h - labelSize.Y) * 0.5f);
            dl.AddText(textPos, ImGui.GetColorU32(UiColors.Text.Rgba), label);
        }

        return clicked;
    }

    public static void AddHint(string label)
    {
        if (string.IsNullOrEmpty(label))
            return;

        AddVerticalSpace(5);
        ApplyIndent();
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f * ImGui.GetStyle().Alpha);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 20));

        AddIcon(Icon.Tip);

        ImGui.SameLine();
        ImGui.TextWrapped(label);
        //ImGui.Indent(-13);
        ImGui.PopStyleVar(2);
    }

    public static void AddVerticalSpace(float size = 10)
    {
        // This breaks ImGui.Indent for some reason so I commented it out and brought back the Dummy call
        /*ImGui.SetCursorPos( new Vector2( ImGui.GetCursorStartPos().X,
                                         ImGui.GetCursorPosY()
                                         +size * T3Ui.UiScaleFactor));*/
        ImGui.Dummy(new Vector2(1, size * T3Ui.UiScaleFactor));
    }

    #region layout helpers

    public static void SetIndent(float newIndent)
    {
        _paramIndent = newIndent;
    }
    
    public static void Indent(float delta)
    {
        _paramIndent += delta;
    }
    
    public static void Unindent(float delta)
    {
        _paramIndent -= delta;
    }

    public static void SetIndentToLeft()
    {
        _paramIndent = 0;
    }

    public static void SetIndentToParameters()
    {
        _paramIndent = DefaultParameterIndent;
    }

    public static void ApplyIndent()
    {
        ImGui.SetCursorPosX(LeftParameterPadding + ParameterSpacing);
    }

    public static void SetWidth(float ratio)
    {
        _widthRatio = ratio;
    }

    public static void ResetWidth()
    {
        _widthRatio = 1;
    }

    public static void DrawInputLabel(string? label)
    {
        if (string.IsNullOrEmpty(label) || label.StartsWith("##"))
            return;

        var labelSize = ImGui.CalcTextSize(label);
        var p = ImGui.GetCursorPos();
        ImGui.SetCursorPosX(MathF.Max(LeftParameterPadding - labelSize.X, 0) + 10);
        ImGui.AlignTextToFramePadding();

        ImGui.TextUnformatted(label);
        ImGui.SetCursorPos(p);

        ImGui.SameLine();
        SetCursorToParameterEdit();
    }

    public static void SetCursorToParameterEdit()
    {
        ImGui.SetCursorPosX(LeftParameterPadding + ParameterSpacing);
    }

    public static bool DrawValueRangeControl(ref float min, ref float max, ref float scale, ref bool clampedMin,
        ref bool clampedMax, float defaultMin,
        float defaultMax, float defaultScale)
    {
        var modified = false;
        var flexWidth = ComputeFlexWidth(2, 3);
        if (CustomComponents.RoundedIconButton("clampMin",
                clampedMin ? Icon.ClampMinOn : Icon.ClampMinOff, 0,
                ImDrawFlags.RoundCornersLeft,
                clampedMin
                    ? CustomComponents.ButtonStates.NeedsAttention
                    : CustomComponents.ButtonStates.Default))
        {
            modified = true;
            clampedMin = !clampedMin;
        }

        modified |= SimpleFloatEdit(1, ref min, defaultMin, flexWidth);
        modified |= SimpleFloatEdit(2, ref scale, defaultScale, flexWidth);
        modified |= SimpleFloatEdit(3, ref max, defaultMax, flexWidth);

        ImGui.SameLine();

        if (CustomComponents.RoundedIconButton("clampMax",
                clampedMax ? Icon.ClampMaxOn : Icon.ClampMaxOff, 0,
                ImDrawFlags.RoundCornersRight,
                clampedMax
                    ? CustomComponents.ButtonStates.NeedsAttention
                    : CustomComponents.ButtonStates.Default))
        {
            modified = true;
            clampedMax = !clampedMax;
        }

        return modified;
    }

    // TODO: This could become obsolete if SingleValueEdit would handle fading on default
    private static bool SimpleFloatEdit(int id, ref float max, float defaultValue, float flexWidth)
    {
        var modified = false;
        ImGui.SameLine();
        ImGui.PushID(id);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, Math.Abs(max - defaultValue) < 0.0001f ? 0.5f : 1.0f);
        if (SingleValueEdit.Draw(ref max,
                new Vector2(flexWidth, ImGui.GetFrameHeight()),
                format: "{0:G7}",
                defaultValue: defaultValue, horizontalAlign: 0.5f)
            .HasFlag(InputEditStateFlags.Modified))
        {
            modified = true;
        }

        ImGui.PopStyleVar();
        ImGui.PopID();
        return modified;
    }

    public static bool DrawIntValueRangeControl(ref int min, ref int max, ref float scale, ref bool clampedMin,
        ref bool clampedMax)
    {
        var modified = false;
        var flexWidth = ComputeFlexWidth(2, 3);
        if (CustomComponents.RoundedIconButton("clampMin",
                clampedMin ? Icon.ClampMinOn : Icon.ClampMinOff, 0,
                ImDrawFlags.RoundCornersLeft,
                clampedMin
                    ? CustomComponents.ButtonStates.NeedsAttention
                    : CustomComponents.ButtonStates.Default))
        {
            modified = true;
            clampedMin = !clampedMin;
        }

        modified |= SimpleIntEdit(1, ref min, int.MinValue, flexWidth);
        modified |= SimpleFloatEdit(2, ref scale, 0, flexWidth);
        modified |= SimpleIntEdit(3, ref max, int.MaxValue, flexWidth);

        ImGui.SameLine();

        if (CustomComponents.RoundedIconButton("clampMax",
                clampedMax ? Icon.ClampMaxOn : Icon.ClampMaxOff, 0,
                ImDrawFlags.RoundCornersRight,
                clampedMax
                    ? CustomComponents.ButtonStates.NeedsAttention
                    : CustomComponents.ButtonStates.Default))
        {
            modified = true;
            clampedMax = !clampedMax;
        }

        return modified;
    }

    // TODO: This could become obsolete if SingleValueEdit would handle fading on default
    private static bool SimpleIntEdit(int id, ref int value, int defaultValue, float flexWidth)
    {
        var modified = false;
        ImGui.SameLine();
        ImGui.PushID(id);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, value == defaultValue ? 0.5f : 1.0f);
        if (SingleValueEdit.Draw(ref value,
                new Vector2(flexWidth, ImGui.GetFrameHeight()), defaultValue: defaultValue, horizontalAlign: 0.5f)
            .HasFlag(InputEditStateFlags.Modified))
        {
            modified = true;
        }

        ImGui.PopStyleVar();
        ImGui.PopID();
        return modified;
    }


    /**
     * Computes the fill width for input group segments
     */
    private static float ComputeFlexWidth(int fixedWidthItemCount, int flexItemCount)
    {
        var totalWidth = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetFrameHeight();
        return (totalWidth - fixedWidthItemCount * height) / flexItemCount;
    }

    private static void DrawWarningBelowField(string? warning)
    {
        if (string.IsNullOrEmpty(warning))
            return;

        ImGui.SetCursorPosX(MathF.Max(LeftParameterPadding, 0) + 20 * T3Ui.UiScaleFactor);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusError.Rgba);
        ImGui.TextUnformatted(warning);
        ImGui.PopStyleColor();
        ImGui.PopFont();
        AddVerticalSpace(4);
    }

    #endregion

    #region internal helpers

    /// <param name="maxWidth">When &gt; 0, caps the requested width (in unscaled px, scaled internally). Used by
    /// number fields so they fill the row on narrow panels but don't stretch across very wide ones. Dropdowns
    /// and string inputs leave this at 0 to keep filling the full width.</param>
    public static Vector2 GetAvailableInputSize(string? tooltip, bool hasReset, bool fillWidth = false,
        float rightPadding = 0, float maxWidth = 0)
    {
        var toolWidth = 20f * T3Ui.UiScaleFactor;
        var sizeForResetToDefault = hasReset ? toolWidth : 0;
        var sizeForTooltip = !string.IsNullOrEmpty(tooltip) ? toolWidth : 0;

        var requestedWidth = fillWidth ? ImGui.GetContentRegionAvail().X * _widthRatio : 200 * T3Ui.UiScaleFactor;
        if (maxWidth > 0)
            requestedWidth = MathF.Min(requestedWidth, maxWidth * T3Ui.UiScaleFactor);
        var availableWidth = MathF.Min(requestedWidth, ImGui.GetContentRegionAvail().X + 20);

        var vector2 = new Vector2(availableWidth
                                  - 20
                                  - rightPadding
                                  - sizeForResetToDefault
                                  - sizeForTooltip,
            ImGui.GetFrameHeight());
        return vector2;
    }

    /// <summary>Draws the small help-marker icon on the current line and shows <paramref name="tooltip"/> when
    /// hovered. Used by the input rows here, and reusable for ad-hoc info markers next to custom labels.</summary>
    internal static void AppendTooltip(string? tooltip)
    {
        if (string.IsNullOrEmpty(tooltip))
            return;

        // A small fixed gap to the input — the default item spacing reads as detached.
        ImGui.SameLine(0, 3 * T3Ui.UiScaleFactor);
        DrawHelpMarker(tooltip);
    }

    private static void DrawHelpMarker(string tooltip)
    {
        ImGui.PushFont(Icons.IconFont);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted("" + (char) Icon.Help);
        ImGui.PopFont();

        if (!ImGui.IsItemHovered())
            return;

        // Tooltip — rendered as Markdown so parameter docs can use **bold**, "- " lists and [OpRef] links.
        // HardLineBreaks keeps plain multi-line tooltips (single "\n") looking as before. The finite max height
        // lets the auto-resizing window fit its content without a spurious scrollbar.
        var scale = T3Ui.UiScaleFactor;
        var width = 280 * scale;
        var maxHeight = ImGui.GetMainViewport().WorkSize.Y - 40 * scale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0), new Vector2(width, maxHeight));
        ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10) * scale);
        ImGui.BeginTooltip();

        // Wrap to a fixed inner width rather than the live content region. The window is auto-resizing,
        // so on its first frame ImGui briefly shows a vertical scrollbar before it knows the content
        // height; that scrollbar steals width, which would reflow the text taller and make the scrollbar
        // stick. A stable wrap width (inner width minus padding and a scrollbar's worth of slack) keeps
        // the layout from reflowing, so the spurious bar disappears on the next frame.
        var innerWrap = width - 2 * 10 * scale - ImGui.GetStyle().ScrollbarSize;
        _tooltipMarkdown.Draw(tooltip, wrapWidthPx: innerWrap);
        ImGui.EndTooltip();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }

    private static readonly MarkdownView _tooltipMarkdown = new(new MarkdownView.Options
                                                                    {
                                                                        MutedBodyText = true,
                                                                        HardLineBreaks = true,
                                                                    });

    internal static bool AppendResetButton(bool hasReset, string? id)
    {
        if (!hasReset)
            return false;

        ImGui.SameLine();
        ImGui.PushID(id ?? "fallback");
        var clicked = CustomComponents.IconButton(Icon.Revert,
            new Vector2(Math.Min(.8f, T3Ui.UiScaleFactor)) * ImGui.GetFrameHeight());
        ImGui.PopID();
        return clicked;
    }

    private static void AddIcon(Icon icon)
    {
        ImGui.PushFont(Icons.IconFont);
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 0.5f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);

        ImGui.TextUnformatted("" + (char) (icon));

        ImGui.PopFont();
        ImGui.PopStyleVar(2);
    }

    #endregion

    private const int NotADefaultValue = Int32.MinValue;

    private const float DefaultParameterIndent = 150;
    private static float _paramIndent = DefaultParameterIndent;
    private static float _widthRatio = 1;
    private static float LeftParameterPadding => _paramIndent * T3Ui.UiScaleFactor;
    public static float ParameterSpacing => 20 * T3Ui.UiScaleFactor;
}
