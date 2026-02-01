using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.SystemUi;

namespace T3.Editor.Gui.Styling;

internal static partial class CustomComponents
{
    public static bool ToggleButton(ref bool isSelected, string label, Vector2 size, bool trigger = false)
    {
        var wasSelected = isSelected;
        var clicked = false;
        if (isSelected)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, isSelected ? UiColors.BackgroundActive.Fade(0.7f).Rgba : UiColors.BackgroundButton.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, isSelected ? UiColors.BackgroundActive.Rgba : UiColors.BackgroundButton.Fade(0.7f).Rgba);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, isSelected ? UiColors.BackgroundActive.Fade(0.7f).Rgba : UiColors.BackgroundButton.Fade(0.7f).Rgba);
        }

        if (ImGui.Button(label, size) || trigger)
        {
            isSelected = !isSelected;
            clicked = true;
        }

        if (wasSelected)
        {
            ImGui.PopStyleColor(4);
        }

        return clicked;
    }

    public static bool ToggleIconButton(ref bool isSelected, Icon icon, Vector2 size, ButtonStates activeState = ButtonStates.Activated)
    {
        var state = isSelected ? activeState : ButtonStates.Dimmed;
        var clicked = IconButton(icon, size, state);
        if (clicked)
            isSelected = !isSelected;

        return clicked;
    }

    public static bool ToggleTwoIconsButton(ref bool isOn,
                                            Icon iconOff,
                                            Icon iconOn,
                                            ButtonStates stateIfOn = ButtonStates.Activated,
                                            ButtonStates stateIfOff = ButtonStates.Activated,
                                            bool isEnabled = true,
                                            bool noBackground= false)
    {
        var state = !isEnabled ? ButtonStates.Disabled
                    : isOn ? stateIfOn : stateIfOff;

        var clicked = noBackground 
                          ? TransparentIconButton(isOn ? iconOn : iconOff, Vector2.Zero, state) 
                          : IconButton(isOn ? iconOn : iconOff, Vector2.Zero, state);
        
        if (clicked && isEnabled)
            isOn = !isOn;

        return clicked;
    }

    public static bool AddSegmentedIconButton<T>(ref T selectedValue, List<Icon> icons) where T : struct, Enum
    {
        var modified = false;
        var selectedValueString = selectedValue.ToString();
        var isFirst = true;
        var enums = Enum.GetValues<T>();

        for (var index = 0; index < enums.Length; index++)
        {
            var icon = icons[index];
            var value = enums[index];

            if (!isFirst)
            {
                ImGui.SameLine();
            }

            var isSelected = selectedValueString == value.ToString();

            var clicked = ToggleIconButton(ref isSelected, icon, Vector2.Zero);
            if (clicked)
            {
                modified = true;
                selectedValue = value;
            }

            if (isSelected)
            {
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                var drawList = ImGui.GetWindowDrawList();
                drawList.AddRectFilled(new Vector2(min.X - 2, max.Y), new Vector2(max.X + 2, max.Y + 2), UiColors.StatusActivated);
            }

            isFirst = false;
        }

        return modified;
    }

    public static bool TransparentIconButton(Icon icon, Vector2 size, ButtonStates state = ButtonStates.Normal)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
        var result = IconButton(icon, size, state);
        ImGui.PopStyleColor();
        return result;
    }
    
    public static bool IconButton(Icon icon, Vector2 size, ButtonStates state = ButtonStates.Normal)
    {
        if (size == Vector2.Zero)
        {
            var h = ImGui.GetFrameHeight();
            size = new Vector2(h);
        }

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundButtonActivated.Rgba);

        if (state == ButtonStates.Activated)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundButtonActivated.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundButtonActivated.Fade(0.8f).Rgba);
        }

        ImGui.PushID((int)icon);
        var clicked = ImGui.Button(string.Empty, size); 
        ImGui.PopID();
        Icons.DrawIconOnLastItem(icon, GetStateColor(state).Rgba);

        ImGui.PopStyleColor();

        if (state == ButtonStates.Activated)
            ImGui.PopStyleColor(2);

        ImGui.PopStyleVar(1);
        return clicked;
    }

    /// <summary>
    /// An override that allows to pass rounded corner flags to have segmented buttons rounded on the outer edges.
    /// </summary>
    public static bool RoundedIconButton(string id, Icon icon, float width, ImDrawFlags corners = ImDrawFlags.RoundCornersNone,
                                         ButtonStates state = ButtonStates.Normal, bool triggered = false)
    {
        var iconColor = GetStateColor(state);

        var size = new Vector2(width, ImGui.GetFrameHeight());
        if (width == 0)
            size.X = size.Y;

        triggered |= ImGui.InvisibleButton(id, size);

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), GetButtonStateBackgroundColor(), 7, corners);

        Icons.DrawIconOnLastItem(icon, iconColor);
        return triggered;
    }

    public static bool StateButton(string label, ButtonStates state = ButtonStates.Normal)
    {
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundButtonActivated.Rgba);

        if (state != ButtonStates.Normal)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GetStateColor(state).Rgba);
            if (state == ButtonStates.Activated)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundButtonActivated.Rgba);
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundButtonActivated.Fade(0.8f).Rgba);
            }
        }

        ImGui.AlignTextToFramePadding();
        var clicked = ImGui.Button(label);

        if (state != ButtonStates.Normal)
            ImGui.PopStyleColor();

        if (state == ButtonStates.Activated)
            ImGui.PopStyleColor(2);

        ImGui.PopStyleColor(1);
        return clicked;
    }

    private static Color GetStateColor(ButtonStates state)
    {
        return state switch
                   {
                       ButtonStates.Dimmed         => UiColors.TextMuted.Fade(0.8f),
                       ButtonStates.Disabled       => UiColors.TextDisabled.Fade(0.6f),
                       ButtonStates.Activated      => UiColors.StatusActivated,
                       ButtonStates.NeedsAttention => UiColors.StatusAttention,
                       _                           => UiColors.Text
                   };
    }



    private static Color GetButtonStateBackgroundColor()
    {
        Color backgroundColor;

        if (ImGui.IsItemActive())
        {
            backgroundColor = ImGuiCol.ButtonActive.GetStyleColor();
        }
        else if (ImGui.IsItemHovered())
        {
            backgroundColor = ImGuiCol.ButtonHovered.GetStyleColor();
        }
        else
        {
            backgroundColor = ImGuiCol.Button.GetStyleColor();
        }

        return backgroundColor;
    }

    public static bool DisablableButton(string label, bool isEnabled, bool enableTriggerWithReturn = false)
    {
        if (isEnabled)
        {
            ImGui.PushFont(Fonts.FontBold);
            if (ImGui.Button(label)
                || (enableTriggerWithReturn && ImGui.IsKeyPressed((ImGuiKey)Key.Return)))
            {
                ImGui.PopFont();
                return true;
            }

            ImGui.PopFont();
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 0.1f));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 1, 0.15f));
            ImGui.Button(label);
            ImGui.PopStyleColor(2);
        }

        return false;
    }

    internal static Vector2 GetCtaButtonSize(string label, Icon icon = Icon.None)
    {
        var showIcon = icon != Icon.None;
        //var padding = new Vector2(10, 2);
        ImGui.PushFont(Fonts.FontLarge);
        var size = ImGui.CalcTextSize(label) + CtaButtonPadding * 2;
        if (showIcon)
            size.X += Icons.FontSize;

        ImGui.PopFont();
        return size;
    }
    
    private static Vector2 CtaButtonPadding => new Vector2(10, 2);
    
    internal static bool DrawCtaButton(string label, Icon icon, Color textColor, Color bgColor, Color borderColor)
    {
        var size = GetCtaButtonSize(label, icon);

        var clicked = ImGui.InvisibleButton(label, size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        var isHovered = ImGui.IsItemHovered();

        dl.AddRectFilled(min, max, bgColor.Fade(isHovered ? 0.8f : 1f), 5);
        dl.AddRect(min, max, borderColor, 5);
        dl.AddText(Fonts.FontLarge, Fonts.FontLarge.FontSize, min + CtaButtonPadding,
                   textColor,
                   label);

        var screenPos = new Vector2(max.X - Icons.FontSize - CtaButtonPadding.X / 2,
                                    (max.Y + min.Y) / 2f - Icons.FontSize / 2f + 1
                                   ).Floor();

        if (icon != Icon.None)
            Icons.DrawIconAtScreenPosition(icon, screenPos, dl, textColor);
        
        return clicked;
    }

    internal static bool DrawCtaButton(string label, Icon icon = Icon.None, ButtonStates state = ButtonStates.Normal)
    {
        var textColor = UiColors.Text;
        var bgColor = UiColors.BackgroundButton;
        var borderColor = Color.Transparent;

        switch (state)
        {
            case ButtonStates.Activated:
                textColor = UiColors.ForegroundFull;
                bgColor = UiColors.BackgroundActive;
                borderColor = Color.Transparent;
                break;
            case ButtonStates.Dimmed:
                textColor = UiColors.Text;
                bgColor = Color.Transparent;
                borderColor = UiColors.ForegroundFull.Fade(0.3f);
                break;
        }

        return DrawCtaButton(label, icon, textColor, bgColor, borderColor);
    }


    public enum ButtonStates
    {
        Normal,
        Dimmed,
        Disabled,
        Activated,
        NeedsAttention,
    }

    /// <summary>
    /// Draws an search match underline under the last search item
    /// </summary>
    public static void DrawSearchMatchUnderline(string searchString, ReadOnlySpan<char> strId, Vector2 offset)
    {
        if (string.IsNullOrEmpty(searchString)) 
            return;
        
        var start = strId.IndexOf(searchString, StringComparison.OrdinalIgnoreCase);
        if (start == -1) 
            return;
        
        var span = strId.Slice(start, searchString.Length);
        var sizeMatch = ImGui.CalcTextSize(span);

        var sizeBefore = start > 0 ? ImGui.CalcTextSize(strId[..start])
                             : Vector2.Zero;
                    
        var fontSize = ImGui.GetFontSize();
        var min = 
            //ImGui.GetItemRectMin() 
                  offset
                  + new Vector2( sizeBefore.X, fontSize) ;
                    
        ImGui.GetWindowDrawList().AddLine(min, min + new Vector2(sizeMatch.X,0), UiColors.BackgroundActive);
    }
}