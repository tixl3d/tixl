﻿namespace T3.Editor.Gui;

/// <summary>
/// A helper class that collects information duration the processing of a frame,
/// so they can be used in the next.   
/// </summary>
internal static class FrameStats
{
    internal static void CompleteFrame()
    {
        (Current, Last) = (Last, Current);
        Current.Clear();
    }

    internal static void AddHoveredId(Guid id)
    {
        Current.HoveredIds.Add(id);
    }

    internal static bool IsIdHovered(Guid id)
    {
        return Last.HoveredIds.Contains(id);
    }

    internal sealed class Stats
    {
        internal bool HasKeyframesBeforeCurrentTime;
        internal bool HasKeyframesAfterCurrentTime;
        internal bool HasAnimatedParameters => HasKeyframesBeforeCurrentTime || HasKeyframesAfterCurrentTime;
        internal bool IsItemContextMenuOpen;
        internal bool OpenedPopupCapturedMouse;
        internal bool OpenedPopupHovered;
        internal bool UiColorsChanged;
        internal bool SomethingWithTooltipHovered;
        internal bool UndoRedoTriggered;
        internal readonly HashSet<Guid> HoveredIds = [];
            
        /// <summary>
        /// This is reset on Frame start and can be useful for allow context menu to stay open even if a
        /// later context menu would also be opened. There is probably some ImGui magic to do this probably. 
        /// </summary>
        internal string OpenedPopUpName;

        internal void Clear()
        {
            HasKeyframesBeforeCurrentTime = false;
            HasKeyframesAfterCurrentTime = false;
            IsItemContextMenuOpen = false;
            UiColorsChanged = false;
            OpenedPopUpName = string.Empty;
            OpenedPopupCapturedMouse = false;
            OpenedPopupHovered = false;
            SomethingWithTooltipHovered = false;
            UndoRedoTriggered = false;
            HoveredIds.Clear();
        }
    }

    internal static Stats Current = new();
    internal static Stats Last = new();
}