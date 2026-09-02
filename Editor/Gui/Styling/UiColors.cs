// ReSharper disable RedundantArgumentDefaultValue

using System.Diagnostics.CodeAnalysis;
using Color = T3.Core.DataTypes.Vector.Color;

namespace T3.Editor.Gui.Styling;

[SuppressMessage("ReSharper", "MemberCanBeInternal")]
[SuppressMessage("Usage", "CA2211:Non-constant fields should not be visible")]
[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class UiColors
{
    // The order and the GroupTitle attributes below drive how the Theme editor groups and lists these
    // colors — keep related colors together and start each group with a GroupTitle. Field names are the
    // serialization keys for saved themes, so reorder freely but never rename a field.

    // -- Full ----------------------------------------------------------------------------------------
    [T3Style.Hint(GroupTitle = "Full",
                  Description = "The fully opaque foreground tone, faded with alpha for most text and icons. Pure white in dark themes, pure black in light themes.")]
    public static Color ForegroundFull = new(1f);

    [T3Style.Hint(Description = "The fully opaque background tone, faded with alpha for shading. Pure black in dark themes.")]
    public static Color BackgroundFull = new(0f, 0f, 0f, 1f);

    // -- Text ----------------------------------------------------------------------------------------
    [T3Style.Hint(GroupTitle = "Text", Description = "Default text color for labels and values.")]
    public static Color Text = new(0.85f);

    [T3Style.Hint(Description = "Secondary text — section headers, hints and less important labels.")]
    public static Color TextMuted = new(0.5f);

    [T3Style.Hint(Description = "Text of disabled / unavailable controls.")]
    public static Color TextDisabled = new(0.2f);

    [T3Style.Hint(Description = "The checkmark inside checkboxes and menu toggles.")]
    public static Color CheckMark = new(1,1,1,0.8f);

    // -- Window Background ---------------------------------------------------------------------------
    [T3Style.Hint(GroupTitle = "Window Background", DisplayName = "Panel Background",
                  Description = "The content background of windows and panels (e.g. the Asset Library, Parameters, Settings content). Kept semi-transparent so stacked panels blend. For the backdrop behind windows and the menu bar, see App / Menu Background.")]
    public static Color WindowBackground = new(0.23f, 0.23f, 0.23f, 0.5f);

    [T3Style.Hint(DisplayName = "Canvas / Editor Background",
                  Description = "The backdrop of the graph editor and the Hub — the large central canvas. Independent from Panel Background so the editing surface can differ from regular panels.")]
    public static Color EditorBackground = new(0.23f, 0.23f, 0.23f, 0.5f);

    [T3Style.Hint(Description = "Background for dialogs, popup windows and context menus.")]
    public static Color BackgroundPopup = new(0.18f, 0.18f, 0.18f, 0.98f);

    [T3Style.Hint(Description = "Outline around popups and context menus.")]
    public static Color PopupBorder = new(0, 0, 0, 1f);

    [T3Style.Hint(DisplayName = "App / Menu Background",
                  Description = "The main application backdrop: the menu bar, the area behind the Hub and floating windows, and the gaps between docked panels. (This is what most people mean by \"the main background\".)")]
    public static Color BackgroundGaps = new(0f, 0f, 0f, 1f);

    [T3Style.Hint(Description = "The small drag handle in the lower-right corner of resizable windows.")]
    public static Color WindowResizeHandle = new (0.00f, 0.00f, 0.00f, 0.25f);

    [T3Style.Hint(Description = "Track behind scrollbars.")]
    public static Color ScrollbarBackground =  new(0.12f, 0.12f, 0.12f, 0.53f);

    [T3Style.Hint(Description = "The draggable scrollbar handle.")]
    public static Color ScrollbarHandle =  new(0.31f, 0.31f, 0.31f, 0.33f);

    [T3Style.Hint(Description = "A neutral gray used for various minor elements.")]
    public static Color Gray = new(0.6f, 0.6f, 0.6f, 1);

    // -- Inputs --------------------------------------------------------------------------------------
    [T3Style.Hint(GroupTitle = "Inputs", Description = "Background of buttons and other clickable controls.")]
    public static Color BackgroundButton = new(0.3f,0.3f,0.3f,0.35f);

    [T3Style.Hint(Description = "Background of a toggle or button while it is switched on.")]
    public static Color BackgroundButtonActivated = new(0.10f,0.10f,0.10f,0.8f);

    [T3Style.Hint(Description = "Hover background for buttons and list rows.")]
    public static Color BackgroundHover = new(0.32f,0.32f,0.32f,0.8f);

    [T3Style.Hint(Description = "Accent highlight for active / selected / pressed elements (the blue).")]
    public static Color BackgroundActive = Color.FromString("#4592FF");

    [T3Style.Hint(Description = "Background of the active tab.")]
    public static Color BackgroundTabActive = Color.FromString("#3A3A3A");

    [T3Style.Hint(Description = "Background of inactive tabs.")]
    public static Color BackgroundTabInActive = Color.FromString("#CC282828");

    [T3Style.Hint(Description = "Background of text / number input fields and dropdowns.")]
    public static Color BackgroundInputField = new(0.08f,0.08f,0.08f,0.8f);

    [T3Style.Hint(Description = "Input field background while hovered.")]
    public static Color BackgroundInputFieldHover = new(0.1f, 0.1f, 0.1f, 1f);

    [T3Style.Hint(Description = "Input field background while being edited.")]
    public static Color BackgroundInputFieldActive = new(0f, 0f, 0f, 1f);

    // -- Various elements ----------------------------------------------------------------------------
    [T3Style.Hint(GroupTitle = "Various elements", Description = "The faint overlay grid on the graph canvas.")]
    public static  Color CanvasGrid = new(0, 0, 0, 0.15f);

    [T3Style.Hint(Description = "Stronger ruler / division lines on the graph and timeline.")]
    public static  Color GridLines = new(0, 0, 0, 0.5f);

    [T3Style.Hint(Description = "Operator dots in the graph mini-map.")]
    public static Color MiniMapItems = new(1f, 1f, 1f, 1f);

    [T3Style.Hint(Description = "Outline of selected items (operators, keyframes, …).")]
    public static Color Selection = new(1f, 1f, 1f, 1f);

    // -- Status colors -------------------------------------------------------------------------------
    [T3Style.Hint(GroupTitle = "Status colors", Description = "Activated / on (blue).")]
    public static Color StatusActivated = Color.FromString("#4592FF");

    [T3Style.Hint(Description = "Driven or linked — input connection or expression (blue).")]
    public static Color StatusAutomated = new(0.08f, 0.48f, 0.7f, 1f);

    [T3Style.Hint(Description = "Controllable by snapshots or other UI features (green).")]
    public static Color StatusControlled = new(0.08f, 0.7f, 0.48f, 1f);

    [T3Style.Hint(Description = "Everything is fine (green).")]
    public static Color StatusOkay = new(154, 199, 32, 255);

    [T3Style.Hint(Description = "Needs attention — errors, recording, muted audio (magenta).")]
    public static Color StatusAttention = new(203, 19, 113, 255);

    [T3Style.Hint(Description = "A non-fatal warning.")]
    public static Color StatusWarning = new(203, 19, 113, 255);

    [T3Style.Hint(Description = "A fatal error.")]
    public static Color StatusError = new(203, 19, 113, 255);

    [T3Style.Hint(Description = "Related to time — animation, keyframes, playback (orange).")]
    public static Color StatusAnimated = new(1f, 0.46f, 0f, 1f);

    // -- Graph operator widgets ----------------------------------------------------------------------
    [T3Style.Hint(GroupTitle = "Graph operator widgets",
                  Description = "Value text shown inside operator widgets on the graph (sliders, knobs, etc.).")]
    public static Color WidgetValueText = new(1, 1, 1, 0.5f);

    [T3Style.Hint(Description = "Title / label text on operator widgets.")]
    public static Color WidgetTitle = new(0.65f);

    [T3Style.Hint(Description = "Widget value text while hovered (brighter).")]
    public static Color WidgetValueTextHover = new(1, 1, 1, 1.2f);

    [T3Style.Hint(Description = "Lines drawn inside widgets, e.g. a slider track.")]
    public static Color WidgetLine = new(1, 1, 1, 0.3f);

    [T3Style.Hint(Description = "Widget lines while hovered.")]
    public static Color WidgetLineHover = new(1, 1, 1, 0.7f);

    [T3Style.Hint(Description = "The zero / axis line in graph widgets.")]
    public static Color WidgetAxis = new(0, 0, 0, 0.3f);

    [T3Style.Hint(Description = "Highlight line for the active / animated value.")]
    public static Color WidgetActiveLine = StatusAnimated;

    [T3Style.Hint(Description = "Strong shading fill inside widgets (the opposite of text, usually applied faded).")]
    public static Color WidgetBackgroundStrong = new(0f, 0f, 0f, 1f);

    [T3Style.Hint(Description = "Bright accent inside widgets.")]
    public static Color WidgetHighlight = new(1f, 1f, 1f, 1f);

    [T3Style.Hint(Description = "Fill of slider handles inside widgets.")]
    public static Color WidgetSlider = new(0.15f);

    // -- Datatype base colors ------------------------------------------------------------------------
    [T3Style.Hint(GroupTitle = "Datatype base colors (adjusted by variations below)",
                  Description = "Base color for number / float values. The variations below derive the actual operator, slot and connection tints from it.")]
    public static Color ColorForValues = new(0.525f, 0.550f, 0.554f, 1.000f);

    [T3Style.Hint(Description = "Base color for text / string data.")]
    public static Color ColorForString = new(0.468f, 0.586f, 0.320f, 1.000f);

    [T3Style.Hint(Description = "Base color for image / texture data.")]
    public static Color ColorForTextures = new (0.625f, 0, 0.54f, 1.000f);

    [T3Style.Hint(Description = "Base color for DX11 resources.")]
    public static Color ColorForDX11 = new(0.84f, 0.46f, 0.44f, 1.000f);

    [T3Style.Hint(Description = "Base color for command / render data.")]
    public static Color ColorForCommands = new(0.132f, 0.722f, 0.762f, 1.000f);

    [T3Style.Hint(Description = "Base color for GPU buffer data.")]
    public static Color ColorForGpuData = new(0.72f, 0.2f, 0.18f, 1.000f);

    [T3Style.Hint(Description = "Base color for CPU-side procedural geometry (MeshGeometry, curves).")]
    public static Color ColorForCpuGeometry = new(0.3f, 0.68f, 0.5f, 1.000f);

    [T3Style.Hint(Description = "Base color for CPU field delegates (ScalarField, VectorField, RemapCurve).")]
    public static Color ColorForCpuFields = new(0.62f, 0.75f, 0.35f, 1.000f);

    [T3Style.Hint(Description = "Base color for shader-graph data.")]
    public static Color ColorForShaderGraph = new(0.82f, 0.26f, 0.7f, 1.000f);
    
    [T3Style.Hint(Description = "Base color for audio-graph data.")]
    public static Color ColorForAudioGraph = new(0.3f, 0.4f, 0.75f, 1.000f);
}