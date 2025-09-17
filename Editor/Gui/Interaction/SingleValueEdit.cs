using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Timing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.SystemUi;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Interaction;

/// <summary>
/// An alternative ImGui component for editing float values 
/// </summary>
internal static class SingleValueEdit
{
    /// <summary>
    /// Wrapper coll for int type 
    /// </summary>
    internal static InputEditStateFlags Draw(ref int value,
                                             Vector2 size,
                                             int min = int.MinValue,
                                             int max = int.MaxValue,
                                             bool clampMin = false,
                                             bool clampMax = false,
                                             float scale = 0.2f,
                                             string format = "{0:0}",
                                             int defaultValue = default,
                                             float horizontalAlign = 0)
    {
        double doubleValue = value;
        var result = Draw(ref doubleValue, size, min, max, clampMin, clampMax, scale: scale, format: format, useIntegers: true, defaultValue: defaultValue, horizontalAlign: horizontalAlign);
        value = (int)doubleValue;
        return result;
    }

    /// <summary>
    /// Wrapper call for float type
    /// </summary>
    internal static InputEditStateFlags Draw(ref float value,
                                             Vector2 size,
                                             float min = float.NegativeInfinity,
                                             float max = float.PositiveInfinity,
                                             bool clampMin = false, 
                                             bool clampMax= false,
                                             float scale = 0.01f,
                                             string format = "{0:0.000}",
                                             float defaultValue = default,
                                             float horizontalAlign = 0)
    {
        double floatValue = value;
        var result = Draw(ref floatValue, size, min, max, clampMin, clampMax, scale: scale, format: format, defaultValue: defaultValue,
                          horizontalAlign: horizontalAlign);
        value = (float)floatValue;
        return result;
    }

    private static int _editInteractionCounter = 0;

    private static InputEditStateFlags Draw(ref double value,
                                            Vector2 size,
                                            double min = double.NegativeInfinity,
                                            double max = double.PositiveInfinity,
                                            bool clampMin = false, 
                                            bool clampMax= false,
                                            float scale = 1,
                                            string format = "{0:0.000}",
                                            bool useIntegers = false,
                                            double defaultValue = default,
                                            float horizontalAlign = 0)
    {
        _numberFormat = format;
        _currentTabIndex++;
        var componentId = ImGui.GetID("valueEdit");

        var shouldFocus = _currentTabIndex == _tabFocusIndex;
        if (shouldFocus)
        {
            //Log.Debug("  ShouldFocus for index " + TabFocusIndex  +  "  state " + _state );
            SetState(InputStates.TextInput);
            _activeJogDialId = componentId;
            _startValue = value;
            _jogDialText = FormatValueForButton(ref value, true);
        }

        var io = ImGui.GetIO();

        if (componentId == _activeJogDialId)
        {
            var modified = false;
            switch (_state)
            {
                case InputStates.Dialing:
                    modified = true;
                    ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundActive.Rgba);
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundActive.Rgba);
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundActive.Rgba);
                    //DrawButtonWithDynamicLabel(FormatValueForButton(ref _editValue), ref size);
                    ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(horizontalAlign,0.5f));
                    ImGui.Button(FormatValueForButton(ref _editValue) + "###button", size);
                    ImGui.PopStyleVar();
                    DrawValueRangeIndicator(value, min, max, true);
                    ImGui.PopStyleColor(3);

                    if (ImGui.IsMouseReleased(0))
                    {
                        var wasClick = ImGui.GetIO().MouseDragMaxDistanceSqr[0] < UserSettings.Config.ClickThreshold;
                        if (wasClick)
                        {
                            if (io.KeyCtrl)
                            {
                                SetState(InputStates.Inactive);
                                return InputEditStateFlags.ResetToDefault;
                            }
                            else
                            {
                                SetState(InputStates.StartedTextInput);
                            }
                        }
                        else
                        {
                            SetState(InputStates.Inactive);
                        }

                        break;
                    }

                    if (ImGui.IsItemDeactivated())
                    {
                        SetState(InputStates.Inactive);
                        break;
                    }

                    var restarted = (float)(ImGui.GetTime() - _timeOpened) < 0.1f;
                    DrawValueEditMethod(ref _editValue, restarted, _center, min, max, clampMin, clampMax, scale);
                    _dampedValue = _slidingAverage.UpdateAndCompute(_editValue);
                    
                    break;

                case InputStates.StartedTextInput:
                    _editInteractionCounter++;
                    ImGui.SetKeyboardFocusHere();
                    SetState(InputStates.TextInput);
                    goto case InputStates.TextInput;

                case InputStates.TextInput:
                    ImGui.PushStyleColor(ImGuiCol.Text, double.IsNaN(_editValue)
                                                            ? UiColors.StatusError.Rgba
                                                            : UiColors.ForegroundFull);
                    ImGui.SetNextItemWidth(size.X);
                    modified =ImGui.InputText("##dialInput" + _editInteractionCounter, ref _jogDialText, 20);

                    // Keep Focusing until Tab-Key released
                    if (shouldFocus)
                    {
                        ImGui.SetKeyboardFocusHere(-1);
                        if (ImGui.IsKeyReleased((ImGuiKey)Key.Tab))
                        {
                            _tabFocusIndex = -1;
                        }
                    }

                    ImGui.PopStyleColor();
                    var completedAfterTabbing = false;
                    if (ImGui.IsKeyPressed((ImGuiKey)Key.Tab) && _tabFocusIndex == -1)
                    {
                        _tabFocusIndex = _currentTabIndex + (ImGui.GetIO().KeyShift ? -1 : 1);
                        completedAfterTabbing = true;
                    }

                    var completedAfterFocusLoss = ImGui.IsKeyPressed((ImGuiKey)Key.Esc) || ImGui.IsItemDeactivated() || !ImGui.IsWindowFocused();

                    var cancelInputAfterFocusLoss = !shouldFocus && !ImGui.IsItemActive();
                    if (cancelInputAfterFocusLoss || completedAfterFocusLoss)
                    {
                        // NOTE: This happens after canceling editing by closing the input
                        // and reopen the state. Sadly there doesn't appear to be a simple fix for this.
                        _tabFocusIndex = -1;
                        modified = false;
                    }

                    if (completedAfterTabbing || completedAfterFocusLoss)
                    {
                        SetState(InputStates.Inactive);
                        if (double.IsNaN(_editValue))
                            _editValue = _startValue;
                        
                        modified = false;
                    }

                    var valid = Evaluate(_jogDialText, ref _editValue);

                    // If the value is invalid, just revert it to what it was previously
                    if (!valid)
                    {
                        _editValue = _startValue;
                        _editValue = defaultValue;
                        _jogDialText = value.ToString(CultureInfo.InvariantCulture);
                    }

                    break;
            }

            //value = _editValue;
            if (_state == InputStates.Dialing)
            {
                value = _dampedValue;
            }
            else
            {
                value = _editValue;
            }
            
            if (_state == InputStates.Inactive)
            {
                return InputEditStateFlags.Finished;
            }
            
            return modified ? InputEditStateFlags.Modified : InputEditStateFlags.Started;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(horizontalAlign,0.5f));
        ImGui.Button(FormatValueForButton(ref value) + "###button", size);
        ImGui.PopStyleVar();

        //DrawButtonWithDynamicLabel(FormatValueForButton(ref value), ref size);
        DrawValueRangeIndicator(value, min, max);

        if (ImGui.IsItemActivated())
        {
            _activeJogDialId = componentId;
            _editValue = value;
            _startValue = value;
            _jogDialText = FormatValueForButton(ref value);
            SetState(InputStates.Dialing);
        }
        else
        {
            var isHovered = ImGui.IsItemHovered();

            if (_state == InputStates.Inactive)
            {
                var isHoveredComponent = _activeHoverComponentId == componentId;
                if (isHoveredComponent)
                {
                    if (isHovered && (!UserSettings.Config.MouseWheelEditsNeedCtrlKey || io.KeyCtrl))
                    {
                        T3Ui.MouseWheelFieldHovered = true;
                        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
                        var dl = ImGui.GetForegroundDrawList();
                        dl.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), UiColors.StatusActivated);

                        var wheel = io.MouseWheel;
                        if (wheel == 0)
                            return InputEditStateFlags.Nothing;


                        if (useIntegers)
                        {
                            value += wheel * (io.KeyAlt ? 10 : 1);
                        }
                        else
                        {
                            var factor = 1f;
                            if (io.KeyShift)
                            {
                                factor = 0.01f;
                            }
                            else if (io.KeyAlt)
                            {
                                factor = 10f;
                            }
                            value += wheel * scale * 10 * factor;
                        }
                        _hoveredComponentModifiedByWheel = true;
                        return InputEditStateFlags.Modified;
                    }

                    var didModify = _hoveredComponentModifiedByWheel;
                    _hoveredComponentModifiedByWheel = false;

                    return didModify
                               ? InputEditStateFlags.ModifiedAndFinished
                               : InputEditStateFlags.Nothing;
                }

                if (isHovered)
                {
                    T3Ui.MouseWheelFieldHovered = true;
                    _hoveredComponentModifiedByWheel = false;
                    _activeHoverComponentId = componentId;
                    return InputEditStateFlags.Started;
                }
            }
        }

        return InputEditStateFlags.Nothing;
    }

    internal static void DrawValueEditMethod(ref double editValue, bool restarted, Vector2 center, double min, double max, bool clampMin, bool clampMax,
                                             float scale)
    {
        switch (UserSettings.Config.ValueEditMethod)
        {
            case UserSettings.ValueEditMethods.InfinitySlider:
                InfinitySliderOverlay.Draw(ref editValue, restarted, center, min, max, scale, clampMin, clampMax);
                break;
            case UserSettings.ValueEditMethods.RadialSlider:
                RadialSliderOverlay.Draw(ref editValue, restarted, center, min, max, scale, clampMin, clampMax);
                break;
            case UserSettings.ValueEditMethods.JogDial:
                JogDialOverlay.Draw(ref editValue, restarted, center, min, max, scale, clampMin, clampMax);
                break;
            case UserSettings.ValueEditMethods.ValueLadder:
            default:
                SliderLadder.Draw(ref editValue, min, max, scale, (float)(ImGui.GetTime() - _timeOpened), clampMin, clampMax, center);
                break;
        }
    }

    private static bool _hoveredComponentModifiedByWheel;
    

    private static readonly SlidingAverage _slidingAverage = new(UserSettings.Config.ValueEditSmoothing.Clamp(0,100));
    private static double _dampedValue;

    private static void SetState(InputStates newState)
    {
        switch (newState)
        {
            case InputStates.Inactive:
            {
                _activeJogDialId = 0;
                break;
            }

            case InputStates.Dialing:
                _slidingAverage.Clear(UserSettings.Config.ValueEditSmoothing);
                 
                _center = ImGui.GetMousePos();
                _timeOpened = ImGui.GetTime();
                break;

            case InputStates.StartedTextInput:
                break;

            case InputStates.TextInput:
                break;
        }

        _state = newState;
    }

    private static int _currentTabIndex;
    private static int _lastMaxTabIndex;
    private static int _tabFocusIndex = -1; // if not -1 tries to set keyboard focus to input field.  

    private static bool Evaluate(string expression, ref double editValue)
    {
        try
        {
            var table = new DataTable();
            table.Columns.Add("expression", typeof(string), expression);
            var row = table.NewRow();
            table.Rows.Add(row);
            var newValue = double.Parse((string)row["expression"]);

            if (double.IsNaN(newValue) || double.IsInfinity(newValue))
                return false;

            editValue = newValue;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static readonly Regex FilterFormattingPattern = new(@".*(\{0:(.+)\}).*");

    private static string FormatValueForButton(ref double value, bool hideSuffix = false)
    {
        // Don't use rounding for integers
        if (_numberFormat == "{0:0}")
            return "" + (int)value;

        try
        {
            if (!hideSuffix)
                return string.Format(_numberFormat, value);

            // Special formatting suffices like ° or × will break the parse and lead to incorrect value input.
            // So we filter them out here.
            var match = FilterFormattingPattern.Match(_numberFormat);

            return string.Format(
                                 match.Success
                                     ? match.Groups[1].Value
                                     : _numberFormat, value);
        }
        catch (FormatException)
        {
            Log.Warning($"Invalid value format '{_numberFormat}'");
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// A horrible ImGui work around to have button that stays active while its label changes.  
    /// </summary>
    private static void DrawButtonWithDynamicLabel(string label, ref Vector2 size)
    {
        var color1 = ImGuiCol.Text.GetStyleColor().Fade(ImGui.GetStyle().Alpha);
        var keepPos = ImGui.GetCursorScreenPos();
        ImGui.Button("##dial", size);
        if (string.IsNullOrEmpty(label))
            return;

        ImGui.GetWindowDrawList().AddText(keepPos + new Vector2(4, 4), color1, label);
    }

    private static void DrawValueRangeIndicator(double value, double min, double max, bool isActive = false)
    {
        if (double.IsInfinity(min) && double.IsInfinity(max)) 
            return;

        var itemSize = ImGui.GetItemRectSize();
        var itemPos = ImGui.GetItemRectMin();

        var center = 0.0;
        if (Math.Abs(min) > 0.001f)
        {
            center = MathUtils.RemapAndClamp(0, min, max, 0, itemSize.X);
            // center = MathUtils.RemapAndClamp((min + max) * 0.5, min, max, 0, itemSize.X);
        }

        var end = MathUtils.RemapAndClamp(value, min, max, 0, itemSize.X);
        var orgCenter = center;

        if (center > end)
        {
            (center, end) = (end, center);
        }

        var p1 = itemPos + new Vector2((float)center, 0);
        var p2 = itemPos + new Vector2((float)end, itemSize.Y);
        
        var areaColor = UiColors.ForegroundFull.Fade(isActive ? 0.5f : 0.05f);
        var centerColor = UiColors.ForegroundFull.Fade(isActive ? 0.5f : 0.1f);
        var symbolColor = UiColors.ForegroundFull.Fade(isActive ? 0.8f : 0.06f);
        ImGui.GetWindowDrawList().AddRectFilled(p1, p2, areaColor);

        // Indicate center
        var alignment = center < orgCenter ? -1 : 0;
        ImGui.GetWindowDrawList().AddRectFilled(
                                                ImGui.GetItemRectMin() + new Vector2((float)orgCenter + alignment, 0),
                                                ImGui.GetItemRectMin() + new Vector2((float)orgCenter + alignment + 1, itemSize.Y),
                                                centerColor);

        // Indicate overflow
        if (value < min)
        {
            var triangleCenter = new Vector2(itemPos.X + 5, itemPos.Y + itemSize.Y - 5);
            ImGui.GetWindowDrawList().AddTriangleFilled(
                                                        triangleCenter + new Vector2(-3, 0),
                                                        triangleCenter + new Vector2(2, -4),
                                                        triangleCenter + new Vector2(2, 4),
                                                        symbolColor
                                                       );
        }
        else if (value > max)
        {
            var triangleCenter = new Vector2(itemPos.X + itemSize.X - 3, itemPos.Y + itemSize.Y - 5);
            ImGui.GetWindowDrawList().AddTriangleFilled(
                                                        triangleCenter + new Vector2(-2, -4),
                                                        triangleCenter + new Vector2(3, 0),
                                                        triangleCenter + new Vector2(-2, 4),
                                                        symbolColor
                                                       );
        }
    }

    private enum InputStates
    {
        Inactive,
        Dialing,
        StartedTextInput,
        TextInput,
    }

    private static uint _activeJogDialId;
    private static uint _activeHoverComponentId;
    private static Vector2 _center;
    private static double _editValue;
    private static double _startValue;
    private static string _jogDialText = "";
    private static InputStates _state = InputStates.Inactive;

    private static string _numberFormat = "{0:0.000}";
    private static double _timeOpened;
    private const double DefaultValue = 0.0;

    /// <summary>
    /// This is a horrible attempt to work around imguis current limitation that button elements can't have a tab focus
    /// </summary>
    public static void StartNextFrame()
    {
        _lastMaxTabIndex = _currentTabIndex;
        _currentTabIndex = 0;

        if (_tabFocusIndex > _lastMaxTabIndex)
        {
            Log.Debug("fixing tab overflow");
            _tabFocusIndex = -1;
        }
    }
}