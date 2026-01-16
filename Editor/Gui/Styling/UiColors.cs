// ReSharper disable RedundantArgumentDefaultValue

using System.Diagnostics.CodeAnalysis;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.Styling;

[SuppressMessage("ReSharper", "MemberCanBeInternal")]
[SuppressMessage("Usage", "CA2211:Non-constant fields should not be visible")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class UiColors
{
    // Text
    [T3Style.Hint(Description = "The opaque foreground color which faded with alpha. In dark themes this should be pure white. And pure black for light themes.")]    
    public static Color ForegroundFull = new(1f);
    [T3Style.Hint(Description = "The opaque background color which faded with alpha. In dark themes this should be pure black.")]
    public static Color BackgroundFull = new(0f, 0f, 0f, 1f);
    
    [T3Style.Hint(GroupTitle = "Text")]
    public static Color Text = new(0.9f);
    public static Color TextMuted = new(0.5f);
    public static Color TextDisabled = new(0.2f);
    public static Color CheckMark = new(1,1,1,0.8f);

    [T3Style.Hint(GroupTitle = "Backgrounds" ,Description = "This is the primary background used for canvases and windows. Don't use transparency for this!")]
    public static Color WindowBackground = new(0.23f, 0.23f, 0.23f, 0.5f);
    
    [T3Style.Hint(Description = "Background color for dialogs, popup windows and context menus.")]
    public static Color BackgroundPopup = new(0.18f, 0.18f, 0.18f, 0.98f);
    public static Color PopupBorder = new(0, 0, 0, 1f);
    
    [T3Style.Hint(Description = "Buttons and form inputs.")]
    public static Color BackgroundButton = new(0.3f,0.3f,0.3f,0.5f);
    public static Color BackgroundButtonActivated = new(0.10f,0.10f,0.10f,0.8f);
    public static Color BackgroundHover = new(0.32f,0.32f,0.32f,0.8f);
    
    [T3Style.Hint(Description = "Highlight color for pressed for activated ui elements.")]
    public static Color BackgroundActive = Color.FromString("#4592FF");
    
    public static Color BackgroundTabActive = Color.FromString("#3A3A3A");
    public static Color BackgroundTabInActive = Color.FromString("#CC282828");
    public static Color BackgroundInputField = new(0.08f,0.08f,0.08f,0.8f);
    public static Color BackgroundInputFieldHover = new(0.1f, 0.1f, 0.1f, 1f);
    public static Color BackgroundInputFieldActive = new(0f, 0f, 0f, 1f);
    
    [T3Style.Hint(Description = "A neutral color used for some ui elements.")]
    public static Color Gray = new(0.6f, 0.6f, 0.6f, 1);
    
    [T3Style.Hint(Description = "The small UI element in the lower right corner of resizable components and windows.")]
    public static Color WindowResizeHandle = new (0.00f, 0.00f, 0.00f, 0.25f);
    
    [T3Style.Hint(Description = "Background for menu, gaps and separators")]
    public static Color BackgroundGaps = new(0f, 0f, 0f, 1f);
    
    public static Color ScrollbarBackground =  new(0.12f, 0.12f, 0.12f, 0.53f);
    public static Color ScrollbarHandle =  new(0.31f, 0.31f, 0.31f, 0.33f);
    
    [T3Style.Hint(GroupTitle = "Various elements", Description = "The overlay grid on for the graph canvas.")]
    public static  Color CanvasGrid = new(0, 0, 0, 0.15f);
    public static  Color GridLines = new(0, 0, 0, 0.5f);
    public static Color MiniMapItems = new(1f, 1f, 1f, 1f);

    
    [T3Style.Hint(GroupTitle = "Status colors")]
    public static Color StatusActivated = Color.FromString("#4592FF");
    public static Color StatusAutomated = new(0.08f, 0.48f, 0.7f, 1f);
    public static Color StatusControlled = new(0.08f, 0.7f, 0.48f, 1f);
    
    public static Color StatusAttention = new(203, 19, 113, 255);
    public static Color StatusWarning = new(203, 19, 113, 255);
    public static Color StatusError = new(203, 19, 113, 255);
    public static Color StatusAnimated = new(1f, 0.46f, 0f, 1f);
    public static Color Selection = new(1f, 1f, 1f, 1f);

    // Widget
    [T3Style.Hint(GroupTitle = "Graph Operator widgets",Description = "Fill color for something")]
    public static Color WidgetValueText = new(1, 1, 1, 0.5f);
    public static Color WidgetTitle = new(0.65f);
    public static Color WidgetValueTextHover = new(1, 1, 1, 1.2f);
    public static Color WidgetLine = new(1, 1, 1, 0.3f);
    public static Color WidgetLineHover = new(1, 1, 1, 0.7f);
    public static Color WidgetAxis = new(0, 0, 0, 0.3f);
    public static Color WidgetActiveLine = StatusAnimated;
    
    [T3Style.Hint(Description = "The opposite of text color.\nMostly applied faded for shading effects.")]
    public static Color WidgetBackgroundStrong = new(0f, 0f, 0f, 1f);
    public static Color WidgetHighlight = new(1f, 1f, 1f, 1f);
    public static Color WidgetSlider = new(0.15f);
    

    [T3Style.Hint(GroupTitle = "Datatype base colors (adjusted by variations below)",Description = "Fill color for something")]
    public static Color ColorForValues = new(0.525f, 0.550f, 0.554f, 1.000f);
    public static Color ColorForString = new(0.468f, 0.586f, 0.320f, 1.000f);
    public static Color ColorForTextures = new (0.625f, 0, 0.54f, 1.000f);
    public static Color ColorForDX11 = new(0.84f, 0.46f, 0.44f, 1.000f);
    public static Color ColorForCommands = new(0.132f, 0.722f, 0.762f, 1.000f);
    public static Color ColorForGpuData = new(0.72f, 0.2f, 0.18f, 1.000f);
    public static Color ColorForShaderGraph = new(0.82f, 0.26f, 0.7f, 1.000f);
}