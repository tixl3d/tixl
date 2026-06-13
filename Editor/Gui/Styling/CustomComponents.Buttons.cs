using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.App;
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
        var state = isSelected ? activeState : ButtonStates.Default;
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

    /// <summary>
    /// Kept for call-site compatibility — <see cref="IconButton(Icon,Vector2,ButtonStates)"/> is
    /// already transparent, so this just forwards to it.
    /// </summary>
    public static bool TransparentIconButton(Icon icon, Vector2 size, ButtonStates state = ButtonStates.Default)
    {
        return IconButton(icon, size, state);
    }

    /// <summary>
    /// Toggleable filter chip: all-caps small text, rounded pill, distinct
    /// colors for active vs. inactive. Returns true on click — caller toggles
    /// the active flag. Designed for tag-style filter rows (test runner Pick
    /// state, Console log severity filter, …).
    /// </summary>
    public static bool TagFilterToggle(string label, bool active)
    {
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8 * T3Ui.UiScaleFactor);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,
                           new Vector2(8 * T3Ui.UiScaleFactor, 2 * T3Ui.UiScaleFactor));

        if (active)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.StatusActivated.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.StatusActivated.Fade(0.85f).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.StatusActivated.Rgba);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Rgba);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, UiColors.BackgroundButton.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.BackgroundHover.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundButtonActivated.Rgba);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        }

        var clicked = ImGui.Button(label.ToUpperInvariant());

        ImGui.PopStyleColor(4);
        ImGui.PopStyleVar(2);
        ImGui.PopFont();
        return clicked;
    }
    
    /// <summary>
    /// The canonical tool icon: transparent background with a subtle rounded hover, so it stays
    /// quiet in toolbars and panels. <see cref="ButtonStates.Activated"/> draws a filled
    /// background to read as "on". Icon tint comes from <see cref="GetStateColor"/>.
    /// </summary>
    public static bool IconButton(Icon icon, Vector2 size, ButtonStates state = ButtonStates.Default)
    {
        if (size == Vector2.Zero)
            size = new Vector2(ImGui.GetFrameHeight());

        var isActivated = state == ButtonStates.Activated;
        // A disabled icon isn't meant to be clicked, so it gets no hover feedback (tooltip still shows).
        PushToolIconStyle(GetRestingIconBackground(isActivated),
                          showHover: state != ButtonStates.Disabled);

        ImGui.PushID((int)icon);
        var clicked = ImGui.Button("##iconBtn", size);
        ImGui.PopID();
        Icons.DrawIconOnLastItem(icon, GetStateColor(state).Rgba);

        PopToolIconStyle();
        return clicked;
    }

    /// <summary>
    /// Transparent tool icon with an explicit tint (used where the color isn't a
    /// <see cref="ButtonStates"/>, e.g. type-colored or status-colored icons).
    /// </summary>
    public static bool IconButton(Icon icon, Vector2 size, Color color)
    {
        if (size == Vector2.Zero)
            size = new Vector2(ImGui.GetFrameHeight());

        PushToolIconStyle(GetRestingIconBackground(isActivated: false));

        ImGui.PushID((int)icon);
        var clicked = ImGui.Button("##iconBtn", size);
        ImGui.PopID();
        Icons.DrawIconOnLastItem(icon, color.Rgba);

        PopToolIconStyle();
        return clicked;
    }

    /// <summary>
    /// Within this scope tool icons keep a filled background so a toolbar's buttons tile into a
    /// continuous bar shape. Wrap the toolbar's icon cluster and <see cref="PopToolbarIconBackground"/>
    /// afterwards. Everywhere else tool icons stay transparent.
    /// </summary>
    public static void PushToolbarIconBackground() => _useFilledIconBackground = true;
    public static void PopToolbarIconBackground() => _useFilledIconBackground = false;

    private static Color GetRestingIconBackground(bool isActivated)
    {
        if (isActivated)
            return UiColors.BackgroundButtonActivated;

        return _useFilledIconBackground ? UiColors.BackgroundButton : Color.Transparent;
    }

    private static void PushToolIconStyle(Color background, bool showHover = true)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        // Square corners in toolbars so filled buttons tile into a continuous bar; rounded elsewhere.
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, _useFilledIconBackground ? 0 : 4 * T3Ui.UiScaleFactor);
        ImGui.PushStyleColor(ImGuiCol.Button, background.Rgba);

        // A muted hover for transparent icons (the regular button-hover is too dominant under
        // one); toolbar buttons keep the stronger theme hover to stay button-like.
        Color hover;
        if (!showHover)
            hover = background;
        else if (_useFilledIconBackground)
            hover = UiColors.BackgroundHover;
        else
            hover = UiColors.BackgroundButton.Fade(0.5f);

        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hover.Rgba);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, showHover ? UiColors.BackgroundButtonActivated.Rgba : background.Rgba);
    }

    private static void PopToolIconStyle()
    {
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
    }

    private static bool _useFilledIconBackground;

    /// <summary>
    /// An override that allows to pass rounded corner flags to have segmented buttons rounded on the outer edges.
    /// </summary>
    public static bool RoundedIconButton(string id, Icon icon, float width, ImDrawFlags corners = ImDrawFlags.RoundCornersNone,
                                         ButtonStates state = ButtonStates.Emphasized, bool triggered = false)
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

    public static bool StateButton(string label, ButtonStates state = ButtonStates.Emphasized)
    {
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.BackgroundButtonActivated.Rgba);

        if (state != ButtonStates.Emphasized)
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

        if (state != ButtonStates.Emphasized)
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
                       ButtonStates.Default        => UiColors.TextMuted.Fade(0.8f),
                       ButtonStates.Emphasized     => UiColors.Text,
                       ButtonStates.Disabled       => UiColors.TextDisabled.Fade(0.6f),
                       ButtonStates.Activated      => UiColors.StatusActivated,
                       ButtonStates.NeedsAttention => UiColors.StatusAttention,
                       _                           => UiColors.TextMuted
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
                || (enableTriggerWithReturn && ImGui.IsKeyPressed(Key.Return.ToImGuiKey())))
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

    internal static bool DrawCtaButton(string label, Icon icon = Icon.None, ButtonStates state = ButtonStates.Emphasized)
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
            case ButtonStates.Default:
                textColor = UiColors.Text;
                bgColor = Color.Transparent;
                borderColor = UiColors.ForegroundFull.Fade(0.3f);
                break;
        }

        return DrawCtaButton(label, icon, textColor, bgColor, borderColor);
    }


    public enum ButtonStates
    {
        /// <summary>Slightly faded — the default look for tool icons; contrasts with Emphasized and Activated.</summary>
        Default,

        /// <summary>Slightly brighter, to emphasize a single icon.</summary>
        Emphasized,

        /// <summary>Barely visible and not meant to be clicked, but its tooltip still shows.</summary>
        Disabled,

        /// <summary>Rare, but currently active — e.g. "has project settings", "show gizmos", "loop mode", console auto-scroll.</summary>
        Activated,

        /// <summary>Temporarily important and easy to overlook — e.g. "audio muted", "currently rendering". A remove icon only needs this on hover.</summary>
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