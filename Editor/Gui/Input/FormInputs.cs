#nullable enable
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Styling;
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
        
        if(useParamIndent)
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
        DrawInputLabel(label);

        ImGui.PushID(label);

        var hasReset = defaultValue != NotADefaultValue;

        var size = GetAvailableInputSize(tooltip, hasReset);
        var result = SingleValueEdit.Draw(ref value, size, min, max, true, true, scale);
        ImGui.PopID();

        AppendTooltip(tooltip);
        if (AppendResetButton(hasReset, label))
        {
            value = defaultValue;
            result |= InputEditStateFlags.ModifiedAndFinished;
        }

        var modified = (result & InputEditStateFlags.Modified) != InputEditStateFlags.Nothing;
        return modified;
    }

    private const float DefaultFadeAlpha = 0.7f;

    public static bool AddFloat(string label,
                                ref float value,
                                float min = float.NegativeInfinity,
                                float max = float.PositiveInfinity,
                                float scale = 0.01f,
                                bool clampMin = false, 
                                bool clampMax= false,
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

        var size = GetAvailableInputSize(tooltip, hasReset, true);

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

        var modified = (result & InputEditStateFlags.Modified) != InputEditStateFlags.Nothing;
        return modified;
    }

    public static bool AddEnumDropdown<T>(ref T selectedValue, string? label, string? tooltip = null, T defaultValue= default) where T : struct, Enum, IConvertible, IFormattable
    {
        DrawInputLabel(label);

        var inputSize = GetAvailableInputSize(tooltip, false, true);
        ImGui.SetNextItemWidth(inputSize.X);

        var modified = DrawEnumDropdown(ref selectedValue, label, defaultValue);

        AppendTooltip(tooltip);

        return modified;
    }

    public static bool DrawEnumDropdown<T>(ref T selectedValue, string? label, T defaultValue= default) where T : struct, Enum, IConvertible, IFormattable, IComparable
    {
        var index = 0;
        var selectedIndex = 0;

        foreach (var n in Enum.GetNames<T>())
        {
            if (n == selectedValue.ToString())
            {
                selectedIndex = index;
                break;
            }

            index++;
        }

        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Text, selectedValue.Equals(defaultValue) ? UiColors.TextMuted.Rgba : UiColors.ForegroundFull.Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);
        var modified = ImGui.Combo($"##dropDown{typeof(T)}{label}", ref selectedIndex, Enum.GetNames<T>(), Enum.GetNames<T>().Length, Enum.GetNames<T>().Length);
        if (modified)
        {
            selectedValue = Enum.GetValues<T>()[selectedIndex];
        }

        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);

        return modified;
    }


    public static bool AddDropdown(ref string selectedValue, IEnumerable<string?> values, string label, string? tooltip = null)
    {
        DrawInputLabel(label);
        var spaceForTooltip = 0f;
        if (tooltip != null) {
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
                                      ImGuiSelectableFlags.DontClosePopups))
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
                // if (!ImGui.Selectable($"{value}", isSelected, ImGuiSelectableFlags.DontClosePopups))
                //     continue;
                
                if (!ImGui.Selectable(getDisplayTextFunc(value), isSelected, ImGuiSelectableFlags.DontClosePopups))
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

    private static readonly Dictionary<string, string> _listBoxAddInputs = new();
    // Dragging state for reorder: persistent across frames while mouse button is down
    private static int _draggingIndex = -1;
    private static bool _isDragging;

    public static bool AddEditableListBox(ref string selectedValue,
                                    IList<string> values,
                                    string label,
                                    Predicate<string>? warningPredicate,
                                    String? warningText,
                                    string? tooltip = null)
     {
         DrawInputLabel(label);

         var imguiLabel = "##ListBox" + label;

         // compute input size early
         var inputSize = GetAvailableInputSize(tooltip, false, true);

         // Compute box height: account for all items plus the bottom transient add-row; add 1.5 lines total so the add-row is visible
         var lineHeight = ImGui.GetTextLineHeightWithSpacing();
         var listBoxHeight = MathF.Min(300, (values.Count + 1.5f) * lineHeight);
         var size = inputSize with { Y = listBoxHeight };

         var modified = false;
         if (ImGui.BeginListBox(imguiLabel, size))
         {
              try
              {
                // Remember the list content start Y so we can compute item indices based on mouse Y while dragging
                var listStartY = ImGui.GetCursorScreenPos().Y;
                  // Top: existing items
                  for (var i = 0; i < values.Count; i++)
                  {
                      ImGui.PushID(i);
                      ImGui.AlignTextToFramePadding();

                      // Drag handle
                      ImGui.Button($"{i}.");
                      // start drag if user presses the handle
                      if (ImGui.IsItemActive() && !_isDragging && ImGui.IsMouseDown(0))
                      {
                          _isDragging = true;
                          _draggingIndex = i;
                      }

                      // continue drag while mouse down
                      if (_isDragging && ImGui.IsMouseDown(0) && _draggingIndex >= 0)
                      {
                          var mouseY = ImGui.GetMousePos().Y;
                          var targetIndex = (int)((mouseY - listStartY) / lineHeight);
                          targetIndex = Math.Clamp(targetIndex, 0, values.Count - 1);
                          if (targetIndex != _draggingIndex)
                          {
                              (values[targetIndex], values[_draggingIndex]) = (values[_draggingIndex], values[targetIndex]);
                              modified = true;
                              _draggingIndex = targetIndex;
                          }
                      }

                      // stop dragging when mouse released
                      if (_isDragging && !ImGui.IsMouseDown(0))
                      {
                          _isDragging = false;
                          _draggingIndex = -1;
                      }

                      ImGui.SameLine(30 * T3Ui.UiScaleFactor);

                      var value = values[i];

                      // warning icon on the left
                      var hasWarning = warningPredicate != null && !warningPredicate(value);
                      if (hasWarning)
                      {
                          AddIcon(Icon.Warning);
                          if (ImGui.IsItemHovered())
                          {
                              ImGui.BeginTooltip();
                              ImGui.TextWrapped(warningText ?? "This entry may cause issues.");
                              ImGui.EndTooltip();
                          }
                          ImGui.SameLine();
                      }

                      // compute reserve space for two small buttons
                      var style = ImGui.GetStyle();
                      var padX = style.FramePadding.X * 2;
                      var wX = ImGui.CalcTextSize("×").X + padX;
                      var wPlus = ImGui.CalcTextSize("+").X + padX;
                      var buttonSpacing = style.ItemSpacing.X;
                      var reservedRight = wX + wPlus + buttonSpacing;

                      // set width for input so buttons fit on same line
                      var regionAvail = ImGui.GetContentRegionAvail().X;
                      ImGui.SetNextItemWidth(MathF.Max(20, regionAvail - reservedRight));

                      var temp = value;
                      if (ImGui.InputText("##item" + i + imguiLabel, ref temp, 1024u))
                      {
                          values[i] = temp;
                          modified = true;
                          if (selectedValue == value)
                              selectedValue = temp;
                      }

                      // draw delete/insert on same line, right after the input
                      ImGui.SameLine();
                      if (ImGui.Button("×"))
                      {
                          if (selectedValue == value)
                              selectedValue = string.Empty;

                          values.RemoveAt(i);
                          modified = true;
                          ImGui.PopID();
                          i--;
                          continue;
                      }

                      ImGui.SameLine();
                      if (ImGui.Button("+"))
                      {
                          values.Insert(i, value);
                          modified = true;
                          i++; // skip the inserted duplicate
                      }

                      ImGui.PopID();
                 }

                 // Bottom: add new item input
                 ImGui.Separator();
                 ImGui.PushID("add" + imguiLabel);
                 if (!_listBoxAddInputs.TryGetValue(imguiLabel, out var addInput))
                    addInput = string.Empty;

                 // Shift cursor horizontally to match the per-item input left offset (drag-handle width)
                 ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 30 * T3Ui.UiScaleFactor);

                 // Compute reserved right width same as per-item (X + + spacing) so plus aligns exactly
                 var styleBottom = ImGui.GetStyle();
                 var padXBottom = styleBottom.FramePadding.X * 2;
                 var wXBottom = ImGui.CalcTextSize("×").X + padXBottom;
                 var wPlusBottom = ImGui.CalcTextSize("+").X + padXBottom;
                 var buttonSpacingBottom = styleBottom.ItemSpacing.X;
                 var reservedRightSame = wXBottom + wPlusBottom + buttonSpacingBottom;

                 var regionAvailBottom = ImGui.GetContentRegionAvail().X;
                 ImGui.SetNextItemWidth(MathF.Max(20, regionAvailBottom - reservedRightSame));

                 if (ImGui.InputText("##add" + imguiLabel, ref addInput, 1024u))
                 {
                    _listBoxAddInputs[imguiLabel] = addInput;
                 }

                 ImGui.SameLine();
                 if (ImGui.Button("+"))
                 {
                    var trimmed = (addInput ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        values.Add(trimmed);
                        selectedValue = trimmed;
                        _listBoxAddInputs[imguiLabel] = string.Empty;
                        modified = true;
                    }
                 }

                 ImGui.PopID();
              }
              finally
              {
                  ImGui.EndListBox();
              }
         }

        AppendTooltip(tooltip);
        return modified;
    }

    public static bool AddSegmentedButtonWithLabel<T>(ref T selectedValue, string label, float columnWidth = 0) where T : struct, Enum
    {
        DrawInputLabel(label);
        return SegmentedButton(ref selectedValue, columnWidth);
    }

    public static bool SegmentedButton<T>(ref T selectedValue, float columnWidth = 0) where T : struct, Enum
    {
        var modified = false;
        var selectedValueString = selectedValue.ToString();
        var isFirst = true;

        foreach (var value in Enum.GetValues<T>())
        {
            var name = CustomComponents.HumanReadablePascalCase(Enum.GetName(value));
            if (!isFirst && columnWidth <= 0)
            {
                ImGui.SameLine();
            }

            var isSelected = selectedValueString == value.ToString();
            var clicked = DrawSelectButton(name, isSelected, columnWidth);

            if (clicked)
            {
                modified = true;
                selectedValue = value;
            }

            isFirst = false;
        }

        return modified;
    }

    private static bool DrawSelectButton(string name, bool isSelected, float width = 0)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, isSelected ? UiColors.BackgroundActive.Fade(0.7f).Rgba : UiColors.BackgroundButton.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, isSelected ? UiColors.BackgroundActive.Rgba : UiColors.BackgroundButton.Fade(0.7f).Rgba);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, isSelected ? UiColors.BackgroundActive.Fade(0.7f).Rgba : UiColors.BackgroundButton.Fade(0.7f).Rgba);

        var clicked = ImGui.Button(name, new Vector2(width, 0));
        ImGui.PopStyleColor(4);
        return clicked;
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
            Log.Error("AddStringInput() requires an id to work. Use ## prefix to hide." );
            label = "##fallback";
        }
            
        var hasDefault = defaultValue != NoDefaultString;
        var isDefault = hasDefault && value == defaultValue;

        if (isDefault)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, DefaultFadeAlpha * ImGui.GetStyle().Alpha);
        }

        DrawInputLabel(label);
        var wasNull = value == null!;   // Support legacy calls
        if (wasNull)
            value = string.Empty;

        var inputSize = GetAvailableInputSize(tooltip, false, true);
        ImGui.SetNextItemWidth(inputSize.X);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);

            
        var modified = ImGui.InputText("##" + label, ref value, 1000);
        if (!modified && wasNull)
            value = null!;  // Support legacy calls 

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
            drawList.AddText(minPos + new Vector2(8, 3)* T3Ui.UiScaleFactor, UiColors.ForegroundFull.Fade(0.25f), placeHolder);
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
            Log.Error("AddStringInput() requires an id to work. Use ## prefix to hide." );
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
            drawList.AddText(minPos + new Vector2(8, 3)* T3Ui.UiScaleFactor, UiColors.ForegroundFull.Fade(0.25f), placeHolder);
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
        var isDefault = defaultValue != null && value == (bool)defaultValue;

        if (isDefault)
        {
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, DefaultFadeAlpha * ImGui.GetStyle().Alpha);
        }

        ImGui.SetCursorPosX(MathF.Max(LeftParameterPadding, 0) + 20 * T3Ui.UiScaleFactor);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        var modified = ImGui.Checkbox(label, ref value);
        ImGui.PopStyleColor();

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
    
    public static void SetCursorToParameterEdit() => ImGui.SetCursorPosX(LeftParameterPadding + ParameterSpacing);
        
    public static bool DrawValueRangeControl(ref float min, ref float max, ref float scale, ref bool clampedMin, ref bool clampedMax, float defaultMin,
                                             float defaultMax, float defaultScale)
    {
        var modified = false;
        var flexWidth = ComputeFlexWidth(2, 3);
        if (CustomComponents.RoundedIconButton("clampMin",
                                        clampedMin ? Icon.ClampMinOn : Icon.ClampMinOff, 0,
                                        ImDrawFlags.RoundCornersLeft,
                                        clampedMin
                                            ? CustomComponents.ButtonStates.NeedsAttention
                                            : CustomComponents.ButtonStates.Dimmed))
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
                                            : CustomComponents.ButtonStates.Dimmed))
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
        
    public static bool DrawIntValueRangeControl(ref int min, ref int max, ref float scale, ref bool clampedMin, ref bool clampedMax)
    {
        var modified = false;
        var flexWidth = ComputeFlexWidth(2, 3);
        if (CustomComponents.RoundedIconButton("clampMin",
                                        clampedMin ? Icon.ClampMinOn : Icon.ClampMinOff, 0,
                                        ImDrawFlags.RoundCornersLeft,
                                        clampedMin
                                            ? CustomComponents.ButtonStates.NeedsAttention
                                            : CustomComponents.ButtonStates.Dimmed))
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
                                            : CustomComponents.ButtonStates.Dimmed))
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
    }
    #endregion

    #region internal helpers
    public static Vector2 GetAvailableInputSize(string? tooltip, bool hasReset, bool fillWidth = false, float rightPadding = 0)
    {
        var toolWidth = 20f * T3Ui.UiScaleFactor;
        var sizeForResetToDefault = hasReset ? toolWidth : 0;
        var sizeForTooltip = !string.IsNullOrEmpty(tooltip) ? toolWidth : 0;

        var requestedWidth = fillWidth ? ImGui.GetContentRegionAvail().X * _widthRatio : 200;
        var availableWidth = MathF.Min(requestedWidth, ImGui.GetContentRegionAvail().X + 20);

        var vector2 = new Vector2(availableWidth
                                  - 20
                                  - rightPadding
                                  - sizeForResetToDefault
                                  - sizeForTooltip,
                                  ImGui.GetFrameHeight());
        return vector2;
    }

    private static void AppendTooltip(string? tooltip)
    {
        if (string.IsNullOrEmpty(tooltip))
            return;

        ImGui.SameLine();

        ImGui.PushFont(Icons.IconFont);
        //ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.5f, 5.5f));
        //ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        //ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 10));
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(""+(char)Icon.Help);

       // ImGui.PopStyleVar();
        ImGui.PopFont();

        //CustomComponents.TooltipForLastItem(tooltip, null, false);
        if (!ImGui.IsItemHovered())
            return;

        // Tooltip
        ImGui.PushStyleColor(ImGuiCol.PopupBg, UiColors.BackgroundFull.Rgba);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1 * ImGui.GetStyle().Alpha);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(300);
        ImGui.TextUnformatted(tooltip);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor();
    }

    private static bool AppendResetButton(bool hasReset, string? id)
    {
        if (!hasReset)
            return false;

        ImGui.SameLine();
        ImGui.PushID(id??"fallback");
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

        ImGui.TextUnformatted("" + (char)(icon));

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
