#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using ImGuiNET;
using T3.Core.DataTypes.Vector;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Implements UI and interaction for dragging or dropping various elements onto imgui items. 
/// </summary>
internal static class DragAndDropHandling
{
    /// <summary>
    /// This should be called once per frame 
    /// </summary>
    internal static void Update()
    {
        var cancelled = IsDragging && _stopRequested;
        if (cancelled)
        {
            FreeData();
            _stopRequested = false;
            _activeDragType = DragTypes.None;
            _externalDropJustHappened = false;
        }

        // An external drop that no drop zone claimed within a frame is discarded. Without this it stays
        // armed until the mouse later passes over *any* drop zone, which then fires the stale drop there
        // (e.g. a file dropped on empty timeline space "landing" in the graph seconds later).
        if (_externalDropJustHappened && ImGui.GetFrameCount() > _externalDropArmedFrame + 1)
        {
            FreeData();
            _activeDragType = DragTypes.None;
            _externalDropJustHappened = false;
        }
    }

    internal static void StartExternalDrag(DragTypes type, string data)
    {
        _activeDragType = type;
        _dataString = data;
        _stopRequested = false;
    }

    internal static void CancelExternalDrag()
    {
        _activeDragType = DragTypes.None;
        _dataString = null;
    }

    internal static void CompleteExternalDrop(DragTypes type, string data)
    {
        _dataString = data;
        _externalDropJustHappened = true;
        _externalDropArmedFrame = ImGui.GetFrameCount();
    }

    /// <summary>
    /// This should be called right after an ImGui item that is a drag source (e.g. a button).
    /// </summary>
    /// <returns>
    /// True if dragging started
    /// </returns>
    internal static bool HandleDragSourceForLastItem(DragTypes dragType, string data)
    {
        if (ImGui.IsItemActive())
        {
            if (IsDragging || !ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
                return false;

            if (HasData)
                FreeData();

            _dataPtr = Marshal.StringToHGlobalUni(data);
            _dataString = data;
            _activeDragType = dragType;

            ImGui.SetDragDropPayload(dragType.ToString(), _dataPtr, (uint)((data.Length + 1) * sizeof(char)));
            ImGui.EndDragDropSource();
            return true;
            
        }

        if (ImGui.IsItemDeactivated())
        {
            StopDragging();
        }

        return false;
    }

    /// <summary>
    /// Can be called after a dropzone.
    /// 
    /// Checks if data matches <see cref="DragTypes"/>, adds an ImGuiDropDropTarget onto
    /// the current ImGui item and show a drop target indicator.
    ///
    /// It's also responsible for cancelling the drag action for events like Escape key presses or focus loss.
    /// </summary>
    /// <returns>
    /// True if dropped
    /// </returns>
    internal static DragInteractionResult TryHandleDropOnItem(DragTypes dragType, out string? data, Action? drawTooltip = null)
    {
        data = string.Empty;
        if (_activeDragType != dragType)
            return DragInteractionResult.None;

        if (!IsDragging && !_externalDropJustHappened)
        {
            _activeDragType = DragTypes.None;
            return DragInteractionResult.None;
        }

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        
        // We can't rely on imgui hovered after drop from external
        var isHovered = _externalDropJustHappened
                            ? new ImRect(min, max).Contains(ImGui.GetMousePos())
                            : ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

        var color = Color.Orange.Fade(isHovered ? 1f : 0.5f);
        var thickness = isHovered ? 2f : 1f;

        ImGui.GetWindowDrawList().AddRect(min, max, color, 3, ImDrawFlags.None, thickness);

        if (!isHovered)
            return DragInteractionResult.None;

        var result = DragInteractionResult.Hovering;
        data = _dataString;

        drawTooltip?.Invoke();

        if (_externalDropJustHappened)
        {
            data = _dataString;
            _stopRequested = true;
            return DragInteractionResult.Dropped;
        }

        if (ImGui.BeginDragDropTarget())
        {
            // Check for manual cancel
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                StopDragging();
                ImGui.EndDragDropTarget();
                return DragInteractionResult.None;
            }

            var payload = ImGui.AcceptDragDropPayload(dragType.ToString());

            // Use MouseReleased(0) to ensure we catch the drop even if IDs are finicky
            if (ImGui.IsMouseReleased(0))
            {
                if (HasData)
                {
                    try
                    {
                        // If it was an internal ImGui-managed drag, the payload data might be relevant
                        // Otherwise, we fall back to our stored _dataString
                        var internalData = Marshal.PtrToStringAuto(payload.Data);
                        data = internalData ?? _dataString;

                        result = data != null
                                     ? DragInteractionResult.Dropped
                                     : DragInteractionResult.Invalid;
                    }
                    catch (Exception e)
                    {
                        Log.Warning(" Failed to get drop data " + e.Message);
                    }
                }

                _stopRequested = true;
            }

            ImGui.EndDragDropTarget();
        }

        return result;
    }

    /// <summary>
    /// Like <see cref="TryHandleDropOnItem"/> but against an explicit screen rect instead of the last
    /// ImGui item — for drop zones larger than any single drawn item (e.g. the whole timeline region).
    /// Internal drags are accepted on mouse release over the rect (no ImGui payload round-trip needed;
    /// the drag data lives in <see cref="_dataString"/> either way).
    /// </summary>
    internal static DragInteractionResult TryHandleDropOnRect(DragTypes dragType, ImRect area, out string? data)
    {
        data = string.Empty;
        if (_activeDragType != dragType)
            return DragInteractionResult.None;

        if (!IsDragging && !_externalDropJustHappened)
        {
            _activeDragType = DragTypes.None;
            return DragInteractionResult.None;
        }

        var isHovered = area.Contains(ImGui.GetMousePos());
        var color = Color.Orange.Fade(isHovered ? 1f : 0.5f);
        ImGui.GetWindowDrawList().AddRect(area.Min, area.Max, color, 3, ImDrawFlags.None, isHovered ? 2f : 1f);

        if (!isHovered)
            return DragInteractionResult.None;

        data = _dataString;

        if (_externalDropJustHappened || ImGui.IsMouseReleased(0))
        {
            _stopRequested = true;
            return DragInteractionResult.Dropped;
        }

        return DragInteractionResult.Hovering;
    }

    internal enum DragInteractionResult
    {
        None,
        Invalid,
        Hovering,
        Dropped,
    }

    /// <summary>
    /// To prevent inconsistencies related to the order of window processing,
    /// we have to defer the end until beginning of 
    /// </summary>
    private static void StopDragging()
    {
        _stopRequested = true;
    }

    private static void FreeData()
    {
        if (!HasData)
            return;

        Marshal.FreeHGlobal(_dataPtr);
        _dataPtr = IntPtr.Zero; // Prevent double free
        _dataString = null;
    }

    private static DragTypes _activeDragType = DragTypes.None;
    internal static bool IsDragging => _activeDragType != DragTypes.None;

    internal static bool IsDraggingWith(DragTypes dragType)
    {
        return _activeDragType == dragType;
    }

    /// <summary>
    /// Peeks at the payload of an active drag without acting as a drop zone —
    /// e.g. for drawing a drag indicator under the mouse.
    /// </summary>
    internal static bool TryGetDragData(DragTypes dragType, [NotNullWhen(true)] out string? data)
    {
        data = _activeDragType == dragType ? _dataString : null;
        return data != null;
    }

    private static bool HasData => _dataPtr != IntPtr.Zero;

    private static bool _externalDropJustHappened; // New flag
    private static int _externalDropArmedFrame;
    private static IntPtr _dataPtr = new(0);
    private static string? _dataString;
    private static bool _stopRequested;

    internal enum DragTypes
    {
        None,
        Symbol,
        FileAsset,
        ExternalFile,
        SymbolNameSpace,
    }
}