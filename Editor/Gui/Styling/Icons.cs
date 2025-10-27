using System.IO;
using System.Runtime.CompilerServices;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Resource;
using T3.Core.Utils;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.Styling;

/// <summary>
/// Handles the mapping of custom icons
/// </summary>
internal static class Icons
{
    public static ImFontPtr IconFont { get; set; }
    public static float FontSize = 16;

    /** Draws icon vertically aligned to current font */
    public static void Draw(this Icon icon)
    {
        var defaultFontSize = ImGui.GetFrameHeight();// ImGui.GetFontSize();
        var glyph = IconFont.FindGlyph((char)icon);
        var iconHeight = glyph.Y0 ; // Not sure if this is correct
        var dy = (int)((defaultFontSize - iconHeight) / 2) + 2;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + dy);
        ImGui.PushFont(IconFont);
        ImGui.TextUnformatted(((char)(int)icon).ToString());
        ImGui.PopFont();
    }

    public static void Draw(Icon icon, Vector2 screenPosition)
    {
        var keepPosition = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(screenPosition);
        Draw(icon);
        ImGui.SetCursorScreenPos(keepPosition);
    }

    public static void Draw(Icon icon, Vector2 screenPosition, Color color)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
        Draw(icon, screenPosition);
        ImGui.PopStyleColor();
    }
    
    public static void DrawIconAtScreenPosition(Icon icon, Vector2 screenPos)
    {
        GetGlyphDefinition(icon, out var uvRange, out var size);
        ImGui.GetWindowDrawList().AddImage(ImGui.GetIO().Fonts.TexID,
                                           screenPos,
                                           screenPos + size,
                                           uvRange.Min,
                                           uvRange.Max,
                                           Color.White);
    }

    public static void DrawIconAtScreenPosition(Icon icon,
                                                Vector2 screenPos,
                                                ImDrawListPtr drawList)
    {
        GetGlyphDefinition(icon, out var uvRange, out var size);
        drawList.AddImage(ImGui.GetIO().Fonts.TexID,
                          screenPos,
                          screenPos + size,
                          uvRange.Min,
                          uvRange.Max,
                          Color.White);
    }

    public static void DrawIconAtScreenPosition(Icon icon,
                                                Vector2 screenPos,
                                                ImDrawListPtr drawList,
                                                Color color)
    {
        GetGlyphDefinition(icon, out var uvRange, out var size);
        drawList.AddImage(ImGui.GetIO().Fonts.TexID,
                          screenPos,
                          screenPos + size,
                          uvRange.Min,
                          uvRange.Max,
                          color);
    }

    public static void DrawIconCenter(Icon icon, Color color, float alignment = 0.5f)
    {
        
        var pos = ImGui.GetItemRectMin();
        var size = ImGui.GetItemRectMax() - pos;
        GetGlyphDefinition(icon, out var uvRange, out var iconSize);
        var centerOffset = MathUtils.Floor((size - iconSize) * new Vector2(alignment, 0.5f));
        var alignedPos = pos + centerOffset;
        ImGui.GetWindowDrawList().AddImage(ImGui.GetIO().Fonts.TexID,
                                           alignedPos,
                                           alignedPos + iconSize,
                                           uvRange.Min,
                                           uvRange.Max,
                                           color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void GetGlyphDefinition(Icon icon, out ImRect uvRange, out Vector2 size)
    {
        ImFontGlyphPtr g = IconFont.FindGlyph((char)icon);
        uvRange = GetCorrectUvRangeFromBrokenGlyphStructure(g);
        size = GetCorrectSizeFromBrokenGlyphStructure(g);
    }

    /// <summary>
    /// It looks like ImGui.net v1.83 returns a somewhat strange glyph definition. 
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ImRect GetCorrectUvRangeFromBrokenGlyphStructure(ImFontGlyphPtr g)
    {
        return new ImRect( //-- U  -- V ---
                          new Vector2(g.X1, g.Y1), // Min    
                          new Vector2(g.U0, g.V0) // Max
                         );
    }

    /// <summary>
    /// It looks like ImGui.net v1.77 returns a somewhat corrupted glyph. 
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 GetCorrectSizeFromBrokenGlyphStructure(ImFontGlyphPtr g)
    {
        return new Vector2(g.X0, g.Y0);
    }
    
    public sealed class IconSource
    {
        public IconSource(Icon icon, int slotIndex)
        {
            SourceArea = ImRect.RectWithSize(new Vector2(SlotSize * slotIndex, 0), new Vector2(15, 15));
            Char = (char)icon;
        }

        public IconSource(Icon icon, int slotIndex, Vector2 size)
        {
            SourceArea = ImRect.RectWithSize(new Vector2(SlotSize * slotIndex, 0), size);
            Char = (char)icon;
        }

        public IconSource(Icon icon, Vector2 pos, Vector2 size)
        {
            SourceArea = ImRect.RectWithSize(pos, size);
            Char = (char)icon;
        }

        private const int SlotSize = 15;
        public readonly ImRect SourceArea;
        public readonly char Char;
    }

    public static readonly IconSource[] CustomIcons =
        {
            new(Icon.None, 0),
            new(Icon.DopeSheetKeyframeLinearSelected, 0, new Vector2(15, 25)),
            new(Icon.DopeSheetKeyframeLinear, 1, new Vector2(15, 25)),
            new(Icon.LastKeyframe, 2, new Vector2(15, 25)),
            new(Icon.FirstKeyframe, 3, new Vector2(15, 25)),
            new(Icon.DopeSheetKeyframeSmoothSelected, 37, new Vector2(15, 25)),
            new(Icon.DopeSheetKeyframeSmooth, 38, new Vector2(15, 25)),

            new(Icon.DopeSheetKeyframeCubicSelected, 39, new Vector2(15, 25)),
            new(Icon.DopeSheetKeyframeCubic, 40, new Vector2(15, 25)),
            new(Icon.DopeSheetKeyframeHorizontalSelected, 41, new Vector2(15, 25)),
            new(Icon.DopeSheetKeyframeHorizontal, 42, new Vector2(15, 25)),

            new(Icon.KeyframeToggleOnBoth, new Vector2(43 * 15, 0), new Vector2(23, 15)),
            new(Icon.KeyframeToggleOnLeft, new Vector2(45 * 15, 0), new Vector2(23, 15)),
            new(Icon.KeyframeToggleOnRight, new Vector2(47 * 15, 0), new Vector2(23, 15)),
            new(Icon.KeyframeToggleOnNone, new Vector2(49 * 15, 0), new Vector2(23, 15)),

            new(Icon.KeyframeToggleOffBoth, new Vector2(43 * 15, 15), new Vector2(23, 15)),
            new(Icon.KeyframeToggleOffLeft, new Vector2(45 * 15, 15), new Vector2(23, 15)),
            new(Icon.KeyframeToggleOffRight, new Vector2(47 * 15, 15), new Vector2(23, 15)),
            new(Icon.KeyframeToggleOffNone, new Vector2(49 * 15, 15), new Vector2(23, 15)),

            new(Icon.CurveKeyframe, 15),
            new(Icon.CurveKeyframeSelected, 16),
            new(Icon.ConstantKeyframeSelected, 25, new Vector2(15, 25)),
            new(Icon.ConstantKeyframe, 26, new Vector2(15, 25)),
            
            new(Icon.JumpToRangeStart, 22),
            new(Icon.JumpToPreviousKeyframe, 23),
            new(Icon.PlayBackwards, 24),
            new(Icon.PlayForwards, 25),
            new(Icon.JumpToNextKeyframe, 26),
            new(Icon.JumpToRangeEnd, 27),
            new(Icon.Loop, 28, new Vector2(24, 15)),
            
            new(Icon.BeatGrid, 30),
            new(Icon.ConnectedInput, 31),
            new(Icon.ConnectedOutput, 32),
            new(Icon.Stripe4PxPattern, 33),
            new(Icon.AddKeyframe, 34),
            new(Icon.PinParams, 35),
            //new(Icon.CurrentTimeMarkerHandle, 17),
            new(Icon.FollowTime, 36),
            new(Icon.ToggleAudioOn, 37),
            new(Icon.ToggleAudioOff, 38),
            new(Icon.HoverPreviewSmall, 39),
            new(Icon.HoverPreviewPlay, 40),
            new(Icon.HoverPreviewDisabled, 41),
            new(Icon.HoverScrub, 42),
            new(Icon.AutoRefresh, 43),
            new(Icon.ChevronLeft, 44),
            new(Icon.ChevronRight, 45),
            new(Icon.ChevronUp, 46),
            new(Icon.ChevronDown, 47),
            new(Icon.Pin, 48),
            new(Icon.PinOutline, 49),
            new(Icon.Unpin, 50),
            new(Icon.HeartOutlined, 51),
            new(Icon.Heart, 52),
            new(Icon.Trash, 53),
            new(Icon.Grid, 54),
            new(Icon.Revert, 55),
            new(Icon.Warning, 56),
            new(Icon.Flame, 57),
            new(Icon.Help, 58),
            new(Icon.Tip, 58),
            new(Icon.Comment, 60),
            new(Icon.IO, 61),
            new(Icon.Presets, 62),
            new(Icon.HelpOutline, 63), 
            new(Icon.Bookmark, slotIndex: 64),
            new(Icon.Settings2, slotIndex: 65),
            new(Icon.Settings, 66),
            new(Icon.Checkmark, 68),
            new(Icon.Refresh, 69),
            new(Icon.Plus, 70),
            new(Icon.Move, 71),
            new(Icon.Scale, 72),
            new(Icon.Rotate, 73),
            new(Icon.Snapshot, 75),
            new(Icon.Camera, 76),
            new(Icon.PlayOutput, 79),
            new(Icon.Pipette, 80),
            new(Icon.Link, 81),
            new(Icon.PopUp, slotIndex: 82),
            // Intentionally left black
            new(Icon.SidePanelRight, slotIndex: 84),
            new(Icon.SidePanelLeft, slotIndex: 85),
            new(Icon.Hub, slotIndex: 86),
            new(Icon.ViewCanvas, slotIndex: 87),
            new(Icon.ViewGrid, slotIndex: 88),
            new(Icon.ViewList, slotIndex: 89),
            new(Icon.ViewParamsList, 90),
            new(Icon.Sorting, slotIndex: 91),
            new(Icon.Knob, slotIndex: 92),
            new(Icon.Search, 93),
            new(Icon.Visible, slotIndex: 94),
            new(Icon.Hidden, slotIndex: 95),
            new(Icon.AddOpToInput, 96),
            new(Icon.ExtractInput, 97),
            new(Icon.OperatorBypassOff, slotIndex: 98),
            new(Icon.OperatorBypassOn, slotIndex: 99),
            new(Icon.OperatorDisabled, slotIndex: 100),
            new(Icon.Dependencies, slotIndex: 101),
            new(Icon.Referenced, slotIndex: 102),
            new(Icon.ClampMinOn, slotIndex: 103),
            new(Icon.ClampMaxOn, slotIndex: 104),
            new(Icon.ClampMinOff, slotIndex: 105),
            new(Icon.ClampMaxOff, slotIndex: 106),
            new(Icon.AddFolder, slotIndex: 107),
            new(Icon.Folder, slotIndex: 108),
            new(Icon.FolderOpen, slotIndex: 109),
            new(Icon.FileImage, slotIndex: 110),
            new(Icon.FileAudio, slotIndex: 111),
            new(Icon.FileVideo, slotIndex: 112),
            new(Icon.FileGeometry, slotIndex: 113),
            new(Icon.FileShader, slotIndex: 114),
            new(Icon.FileT3Font, slotIndex: 115),
            // Intentionally left black
            new(Icon.FileDocument, slotIndex: 117),
            new(Icon.ScrollLog, slotIndex: 118),
            new(Icon.ClearLog, slotIndex: 119),
            new(Icon.CopyToClipboard, slotIndex: 120),
            new(Icon.TreeCollapse, slotIndex: 121),
            new(Icon.TreeExpand, slotIndex: 122),
            new(Icon.Target, slotIndex: 123),
            new(Icon.Aim, slotIndex: 124),
            new(Icon.RotateCounterClockwise, slotIndex: 125),
            new(Icon.RotateClockwise, slotIndex: 126),
            
        };

    public static readonly string IconAtlasPath = Path.Combine(SharedResources.Directory, @"images\editor\t3-icons.png");

    public static readonly Dictionary<float, string> IconFilePathForResolutions
        = new()
              {
                  { 1f, Path.Combine(SharedResources.Directory, @"images\editor\t3-icons.png") },
                  { 2f, Path.Combine(SharedResources.Directory, @"images\editor\t3-icons@2x.png") },
                  { 3f, Path.Combine(SharedResources.Directory, @"images\editor\t3-icons@3x.png") },
              };
}

public enum Icon
{
    None = 0,
    DopeSheetKeyframeLinearSelected = 64,
    DopeSheetKeyframeLinear,
    LastKeyframe,
    FirstKeyframe,
    JumpToRangeStart,
    JumpToPreviousKeyframe,
    PlayBackwards,
    PlayForwards,
    JumpToNextKeyframe,
    JumpToRangeEnd,
    Loop,
    BeatGrid,
    ConnectedInput,
    Stripe4PxPattern,
    CurveKeyframe,
    CurveKeyframeSelected,
    CurrentTimeMarkerHandle,
    FollowTime,
    ToggleAudioOn,
    ToggleAudioOff,
    Warning,
    HoverPreviewSmall,
    HoverPreviewPlay,
    HoverPreviewDisabled,
    ConstantKeyframeSelected,
    ConstantKeyframe,
    ChevronLeft,
    ChevronRight,
    ChevronUp,
    ChevronDown,
    Pin,
    PinOutline,
    Unpin,
    HeartOutlined,
    Heart,
    Trash,
    Grid,
    Revert,
    DopeSheetKeyframeSmoothSelected,
    DopeSheetKeyframeSmooth,
    DopeSheetKeyframeCubicSelected,
    DopeSheetKeyframeCubic,
    DopeSheetKeyframeHorizontalSelected,
    DopeSheetKeyframeHorizontal,
    KeyframeToggleOnBoth,
    KeyframeToggleOnLeft,
    KeyframeToggleOnRight,
    KeyframeToggleOnNone,
    KeyframeToggleOffBoth,
    KeyframeToggleOffLeft,
    KeyframeToggleOffRight,
    KeyframeToggleOffNone,
    Checkmark,
    Settings,
    Refresh,
    Plus,
    HoverScrub,
    AutoRefresh,
    Snapshot,
    Move,
    Scale,
    Rotate,
    Help,
    Tip,
    PinParams,
    Pipette,
    Link,
    Search,
    ViewParamsList,
    Presets,
    HelpOutline,
    PlayOutput,
    AddKeyframe,
    AddOpToInput,
    ExtractInput,
    Flame,
    IO,
    Comment,
    Camera,
    PopUp,
    Visible,
    Hidden,
    ViewGrid,
    ViewList,
    Sorting,
    Settings2,
    SidePanelRight,
    SidePanelLeft,
    OperatorBypassOff,
    OperatorBypassOn,
    OperatorDisabled,
    Knob,
    ClampMinOn,
    ClampMaxOn,
    ClampMinOff,
    ClampMaxOff,
    Bookmark,
    Dependencies,
    Referenced,
    AddFolder,
    FolderOpen,
    Hub,
    Folder,
    FileImage,
    FileDocument,
    FileAudio,
    FileVideo,
    FileGeometry,
    FileShader,
    FileT3Font,
    ScrollLog,
    ClearLog,
    CopyToClipboard,
    TreeCollapse,
    TreeExpand,
    Target,
    Aim,
    RotateClockwise,
    RotateCounterClockwise,
    ConnectedOutput,
    ViewCanvas,
}