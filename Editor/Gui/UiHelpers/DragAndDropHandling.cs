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
            IsDragging = false;
        }
    }
    
    /// <summary>
    /// This should be called right after an ImGui item that is a drag source (e.g. a button).
    /// </summary>
    internal static void HandleDragSourceForLastItem(string dragId, string data, string dragLabel)
    {
        if (ImGui.IsItemActive())
        {
            if (IsDragging || !ImGui.BeginDragDropSource())
                return;
            
            if(HasData)
                FreeData();
            
            _dropData = Marshal.StringToHGlobalUni(data);
            IsDragging = true;

            ImGui.SetDragDropPayload(dragId, _dropData, (uint)((data.Length +1) * sizeof(char)));

            ImGui.Button(dragLabel);
            ImGui.EndDragDropSource();
        }
        else if (ImGui.IsItemDeactivated())
        {
            StopDragging();
        }
    }

    internal static bool TryGetDataDroppedLastItem(string dragId, [NotNullWhen(true)] out string? data)
    {
        data = string.Empty;
        
        if (!IsDragging)
            return false;


        var isHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var fade = isHovered ? 1 : 0.5f;
        
        ImGui.GetForegroundDrawList()
             .AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), 
                      Color.Orange.Fade(fade));

        if (!isHovered)
            return false;
        
        if (!ImGui.BeginDragDropTarget())
            return false;
        
        var success = false;
        var payload = ImGui.AcceptDragDropPayload(dragId);
        if (ImGui.IsMouseReleased(0))
        {
            if (HasData)
            {
                try
                {
                    data = Marshal.PtrToStringAuto(payload.Data);
                    success = data != null;
                }
                catch (Exception e)
                {
                    Log.Warning(" Failed to get drop data " + e.Message);
                }
            }
            else
            {
                Log.Warning("No data for drop?");
            }
            IsDragging = false;
        }

        ImGui.EndDragDropTarget();

        return success;
    }
    
    /// <summary>
    /// To prevent inconsistencies related to the order of window processing,
    /// we have to deferred the end until beginning of 
    /// </summary>
    private static void StopDragging()
    {
        _stopRequested = true;
    }

    private static void FreeData()
    {
        if (!HasData)
            return;
        
        Marshal.FreeHGlobal(_dropData);
        _dropData = IntPtr.Zero; // Prevent double free
    }

    private static bool HasData => _dropData != IntPtr.Zero;
    
    internal static bool IsDragging { get; private set; }

    private static IntPtr _dropData = new(0);
    private static bool _stopRequested;

    internal const string SymbolDraggingId = "symbol";
}