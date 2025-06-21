using System.IO;
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

    /** Draws icon vertically aligned to current font */
    public static void Draw(this Icon icon)
    {
        var defaultFontSize = ImGui.GetFontSize(); 
        var glyph = IconFont.FindGlyph((char)icon);
        var iconHeight = glyph.Y0; // Not sure if this is correct
        var dy = (int)((defaultFontSize - iconHeight) / 2) +2;
        ImGui.SetCursorPosY( ImGui.GetCursorPosY() + dy);
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

    public static void Draw(Icon icon, ImRect area)
    {
        var fonts = ImGui.GetIO().Fonts;
        var g = IconFont.FindGlyph((char)icon);
        ImGui.SetCursorScreenPos(area.Min);
        ImGui.Image(fonts.TexID, area.GetSize(), new Vector2(g.V0, g.U0), new Vector2(g.V1, g.U1));
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

    public static void DrawIconOnLastItem(Icon icon, Color color, float alignment= 0.5f)
    {
        var pos = ImGui.GetItemRectMin();
        var size = ImGui.GetItemRectMax() - pos;
        GetGlyphDefinition(icon, out var uvRange, out var iconSize);
        var centerOffset = MathUtils.Floor( (size - iconSize) * new Vector2( alignment, 0.5f));
        var alignedPos = pos + centerOffset;
        ImGui.GetWindowDrawList().AddImage(ImGui.GetIO().Fonts.TexID,
                                           alignedPos,
                                           alignedPos + iconSize,
                                           uvRange.Min,
                                           uvRange.Max,
                                           color);
        ImGui.GetWindowDrawList().AddImage(ImGui.GetIO().Fonts.TexID,
                                           alignedPos,
                                           alignedPos + iconSize,
                                           uvRange.Min,
                                           uvRange.Max,
                                           color);
    }
        
    private static void GetGlyphDefinition(Icon icon, out ImRect uvRange, out Vector2 size)
    {
        ImFontGlyphPtr g = IconFont.FindGlyph((char)icon);
        uvRange = GetCorrectUvRangeFromBrokenGlyphStructure(g);
        size = GetCorrectSizeFromBrokenGlyphStructure(g);
    }

    /// <summary>
    /// It looks like ImGui.net v1.83 returns a somewhat strange glyph definition. 
    /// </summary>
    private static ImRect GetCorrectUvRangeFromBrokenGlyphStructure(ImFontGlyphPtr g)
    {
        return new ImRect(             //-- U  -- V ---
                          new Vector2(g.X1,   g.Y1),    // Min    
                          new Vector2(g.U0, g.V0)   // Max
                         );
    }

    /// <summary>
    /// It looks like ImGui.net v1.77 returns a somewhat corrupted glyph. 
    /// </summary>
    private static Vector2 GetCorrectSizeFromBrokenGlyphStructure(ImFontGlyphPtr g)
    {
        return new Vector2(g.X0, g.Y0);
    }

    /// <summary>
    /// Draws a icon in the center of the current imgui item
    /// </summary>
    public static void DrawCentered(Icon icon)
    {
        var g = IconFont.FindGlyph((char)icon);
        var iconSize = new Vector2(g.X1 - g.X0, g.Y1 - g.Y0) / 2;
        var center = (ImGui.GetItemRectMax() + ImGui.GetItemRectMin()) / 2 - iconSize;
        Draw(icon, center);
    }

    public sealed class IconSource
    {
        public IconSource(Icon icon, int slotIndex)
        {
            SourceArea = ImRect.RectWithSize(new Vector2(SlotSize * slotIndex, 0), new Vector2(15,15));
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
            new (Icon.None, 0),                
            new (Icon.DopeSheetKeyframeLinearSelected, 0, new Vector2(15, 25)),
            new (Icon.DopeSheetKeyframeLinear, 1, new Vector2(15, 25)),
            new (Icon.LastKeyframe, 2, new Vector2(15, 25)),
            new (Icon.FirstKeyframe, 3, new Vector2(15, 25)),
            new (Icon.JumpToRangeStart, 4),
            new (Icon.JumpToPreviousKeyframe, 5),
            new (Icon.PlayBackwards, 6),
            new (Icon.PlayForwards, 7),
            new (Icon.JumpToNextKeyframe, 8),
            new (Icon.JumpToRangeEnd, 9),
            new (Icon.Loop, 10, new Vector2(24, 15)),
            new (Icon.BeatGrid, 12),
            new (Icon.ConnectedParameter, 13),
            new (Icon.Stripe4PxPattern, 14),
            new (Icon.CurveKeyframe, 15),
            new (Icon.CurveKeyframeSelected, 16),
            new (Icon.CurrentTimeMarkerHandle, 17),
            new (Icon.FollowTime, 18),
            new (Icon.ToggleAudioOn, 19),
            new (Icon.ToggleAudioOff, 20),
            new (Icon.Warning, 21),
            new (Icon.HoverPreviewSmall, 22),
            new (Icon.HoverPreviewPlay, 23),
            new (Icon.HoverPreviewDisabled, 24),
            new (Icon.ConstantKeyframeSelected, 25, new Vector2(15, 25)),
            new (Icon.ConstantKeyframe, 26, new Vector2(15, 25)),
            new (Icon.ChevronLeft, 27),
            new (Icon.ChevronRight, 28),
            new (Icon.ChevronUp, 29),
            new (Icon.ChevronDown, 30),
            new (Icon.Pin, 31),
            new (Icon.HeartOutlined, 32),
            new (Icon.Heart, 33),
            new (Icon.Trash, 34),
            new (Icon.Grid, 35),
            new (Icon.Revert, 36),
                
            new (Icon.DopeSheetKeyframeSmoothSelected, 37, new Vector2(15, 25)),
            new (Icon.DopeSheetKeyframeSmooth, 38, new Vector2(15, 25)),
                
            new (Icon.DopeSheetKeyframeCubicSelected, 39, new Vector2(15, 25)),
            new (Icon.DopeSheetKeyframeCubic, 40, new Vector2(15, 25)),
            new (Icon.DopeSheetKeyframeHorizontalSelected, 41, new Vector2(15, 25)),
            new (Icon.DopeSheetKeyframeHorizontal, 42, new Vector2(15, 25)),
                
            new (Icon.KeyframeToggleOnBoth, new Vector2(43 * 15, 0), new Vector2(23, 15)),
            new (Icon.KeyframeToggleOnLeft, new Vector2(45 * 15, 0), new Vector2(23, 15)),
            new (Icon.KeyframeToggleOnRight, new Vector2(47 * 15, 0), new Vector2(23, 15)),
            new (Icon.KeyframeToggleOnNone, new Vector2(49 * 15, 0), new Vector2(23, 15)),
                
            new (Icon.KeyframeToggleOffBoth, new Vector2(43 * 15, 15), new Vector2(23, 15)),
            new (Icon.KeyframeToggleOffLeft, new Vector2(45 * 15, 15), new Vector2(23, 15)),
            new (Icon.KeyframeToggleOffRight, new Vector2(47 * 15, 15), new Vector2(23, 15)),
            new (Icon.KeyframeToggleOffNone, new Vector2(49 * 15, 15), new Vector2(23, 15)),
            new (Icon.Checkmark,  51),
            new (Icon.Settings,  52),
                
            new (Icon.Refresh,  53),
            new (Icon.Plus,  54),
            new (Icon.HoverScrub,  55),
            new (Icon.AutoRefresh,  56),
            new (Icon.Snapshot,  57),
            new (Icon.Move, 58),
            new (Icon.Scale, 59),
            new (Icon.Rotate, 60),
            new (Icon.Help, 61),
            new (Icon.Hint, 62),                
            new (Icon.PinParams, 63),
            new (Icon.Unpin, 64),
            new (Icon.Pipette, 65),
            new (Icon.Link, 66),
            new (Icon.Search, 67),
            new (Icon.ParamsList, 68),
            new (Icon.Presets, 69),
            new (Icon.HelpOutline, 70),
            new (Icon.PlayOutput, 71),
            new (Icon.AddKeyframe, 72),
            new (Icon.AddOpToInput, 73),
            new (Icon.ExtractInput, 74),
            new (Icon.IO, 75),
            new (Icon.Flame, 76),
            new (Icon.Comment, 77),
            new (Icon.Camera, 78),
            new (Icon.PopUp, slotIndex:79),
            new (Icon.Visible, slotIndex:80),
            new (Icon.Hidden, slotIndex:81),
            new (Icon.Thumbnails, slotIndex:82),
            new (Icon.List, slotIndex:83),
            new (Icon.Sorting, slotIndex:84),
            new (Icon.Settings2, slotIndex:85),
            new (Icon.SidePanelRight, slotIndex:86),
            new (Icon.SidePanelLeft, slotIndex:87),
            new (Icon.OperatorBypassOff, slotIndex:88),
            new (Icon.OperatorBypassOn, slotIndex:89),
            new (Icon.OperatorDisabled, slotIndex:90),
            new (Icon.Knob, slotIndex:91),
            new (Icon.ClampMinOn, slotIndex:92),
            new (Icon.ClampMaxOn, slotIndex:93),
            new (Icon.ClampMinOff, slotIndex:94),
            new (Icon.ClampMaxOff, slotIndex:95),
            new (Icon.Bookmark, slotIndex:96),
            new (Icon.Dependencies, slotIndex:97),
            new (Icon.Referenced, slotIndex:98),
            new (Icon.AddFolder, slotIndex:99),
            new (Icon.Folder, slotIndex:100),
            new (Icon.FolderOpen, slotIndex:101),
            new (Icon.FileImage, slotIndex:102),
            new (Icon.FileDocument, slotIndex:103),
            new (Icon.Hub, slotIndex:104),
        };

    public static readonly string IconAtlasPath = Path.Combine(SharedResources.Directory, @"images\editor\t3-icons.png");
}

public enum Icon
{
    None=0,
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
    ConnectedParameter,
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
    Hint,
    PinParams,
    Unpin,
    Pipette,
    Link,
    Search,
    ParamsList,
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
    Thumbnails,
    List,
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
    Folder,
    FolderOpen,
    FileImage,
    FileDocument,
    Hub
}