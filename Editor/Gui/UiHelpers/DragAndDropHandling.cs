#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using ImGuiNET;
using T3.Core.DataTypes.Vector;

namespace T3.Editor.Gui.UiHelpers;

internal static class DragAndDropHandling
{
    /// <summary>
    /// This should be called once per frame 
    /// </summary>
    internal static void Update()
    {
        if (IsDragging && _stopRequested)
        {
            FreeData();
            _stopRequested = false;
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
    }

    /// <summary>
    /// This should be called right after an ImGui item that is a drag source (e.g. a button).
    /// </summary>
    internal static void HandleDragSourceForLastItem(DragTypes dragType, string data, string dragLabel)
    {
        if (ImGui.IsItemActive())
        {
            if (IsDragging || !ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip))
                return;

            if (HasData)
                FreeData();

            _dataPtr = Marshal.StringToHGlobalUni(data);
            _dataString = data;
            _activeDragType = dragType;

            ImGui.SetDragDropPayload(dragType.ToString(), _dataPtr, (uint)((data.Length + 1) * sizeof(char)));
            ImGui.EndDragDropSource();
        }
        else if (ImGui.IsItemDeactivated())
        {
            StopDragging();
        }
    }

    /// <summary>
    /// Checks if data is valid for the passed DragId
    /// </summary>
    /// <returns>
    /// True if dropped
    /// </returns>
    internal static bool TryHandleItemDrop(DragTypes dragType, out string? data, out DragInteractionResult result, Action? drawTooltip = null)
    {
        data = string.Empty;
        result = DragInteractionResult.None;

        if (_activeDragType != dragType)
            return false;

        if (!IsDragging && !_externalDropJustHappened)
        {
            _activeDragType = DragTypes.None;
            return false;
        }

        var isHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var color = Color.Orange.Fade(isHovered ? 1f : 0.5f);
        var thickness = isHovered ? 2f : 1f;

        ImGui.GetWindowDrawList().AddRect(min, max, color, 0, ImDrawFlags.None, thickness);

        if (!isHovered && !_externalDropJustHappened)
            return false;

        result = DragInteractionResult.Hovering;
        data = _dataString;

        drawTooltip?.Invoke();

        if (_externalDropJustHappened)
        {

            data = _dataString;
            result = DragInteractionResult.Dropped;
            _stopRequested = true;
            return true;
        }

        if (ImGui.BeginDragDropTarget())
        {
            // Check for manual cancel
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                StopDragging();
                ImGui.EndDragDropTarget();
                return false;
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

        return true;
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

    private static bool HasData => _dataPtr != IntPtr.Zero;

    private static bool _externalDropJustHappened; // New flag
    private static IntPtr _dataPtr = new(0);
    private static string? _dataString = null;
    private static bool _stopRequested;

    // TODO: Should be an enumeration
    internal enum DragTypes
    {
        None,
        Symbol,
        FileAsset,
        ExternalFile,
    }
    // internal const string SymbolDraggingId = "symbol";
    // internal const string AssetDraggingId = "fileAsset";
}