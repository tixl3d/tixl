#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows;

/// <summary>
/// Allows users to create and use bookmarks to quickly navigate between areas of graphs
/// </summary>
internal static class GraphBookmarkNavigation
{
    public static void HandleForCanvas(ProjectView components)
    {
        var alreadyInteracted = HasInteractedInCurrentFrame();
        if (alreadyInteracted)
        {
            return;
        }

        for (var i = 0; i < _saveBookmarkActions.Length; i++)
        {
            if (KeyActionHandling.Triggered(_saveBookmarkActions[i]))
            {
                SaveBookmark(components, i);
                _lastInteractionFrame = ImGui.GetFrameCount();
                break;
            }

            else if (KeyActionHandling.Triggered(_loadBookmarkActions[i]))
            {
                LoadBookmark(components, i);
                _lastInteractionFrame = ImGui.GetFrameCount();
                break;
            }
        }
    }

    public static void DrawBookmarksMenu()
    {
        var components = ProjectView.Focused;
        if (components == null)
        {
            return;
        }

        if (ImGui.BeginMenu("Open Bookmark in Graph"))
        {
            for (var index = 0; index < _loadBookmarkActions.Length; index++)
            {
                var action = _loadBookmarkActions[index];
                var shortcuts = action.ListShortcuts();
                var isAvailable = DoesBookmarkExist(index);
                if (ImGui.MenuItem(action.ToString(), shortcuts, false, enabled: isAvailable))
                {
                    LoadBookmark(components, index);
                }
            }

            ImGui.EndMenu();
                
        }

        if (ImGui.BeginMenu("Save Bookmark"))
        {
            for (var index = 0; index < _saveBookmarkActions.Length; index++)
            {
                var action = _saveBookmarkActions[index];
                var shortcuts = action.ListShortcuts();
                    
                if (ImGui.MenuItem(action.ToString(), shortcuts))
                {
                    SaveBookmark(components, index);
                }
            }

            ImGui.EndMenu();
        }
    }

    private static void LoadBookmark(ProjectView window, int index)
    {
        var bookmark = GetBookmarkAt(index);
        if (bookmark == null)
        {
            Log.Debug($"Bookmark {index} doesn't exist. You can create it by focusing a graph window and press Ctrl+Shift" + index);
            return;
        }

        var op = window.Structure.GetInstanceFromIdPath(bookmark.IdPath);
        if (op == null)
        {
            Log.Error("Invalid node path");
            return;
        }

        window.TrySetCompositionOp(bookmark.IdPath);
        window.GraphCanvas.SetTargetView(bookmark.ViewScope.Scale, bookmark.ViewScope.Scroll);
        //SelectionManager.SetSelection(bookmark.SelectedChildIds);
    }

    private static void SaveBookmark(ProjectView window, int index)
    {
        if (window.CompositionInstance == null)
            return;
        
        Log.Debug("Saving bookmark " + index);
        var bookmarks = UserSettings.Config.Bookmarks;

        // Extend list length if necessary
        if (UserSettings.Config.Bookmarks.Count <= index)
        {
            bookmarks.AddRange(new Bookmark[index + 1 - bookmarks.Count]);
        }
            
        var canvas = window.GraphCanvas;

        bookmarks[index] = new Bookmark
                               {
                                   IdPath = window.CompositionInstance.InstancePath.ToList(),
                                   ViewScope = canvas.GetTargetScope(),
                                   SelectedChildIds = window.NodeSelection.GetSelectedNodes<SymbolUi.Child>().Select(s => s.Id).ToList()
                               };
    }

    private static readonly UserActions[] _loadBookmarkActions =
        {
            UserActions.LoadBookmark0,
            UserActions.LoadBookmark1,
            UserActions.LoadBookmark2,
            UserActions.LoadBookmark3,
            UserActions.LoadBookmark4,
            UserActions.LoadBookmark5,
            UserActions.LoadBookmark6,
            UserActions.LoadBookmark7,
            UserActions.LoadBookmark8,
            UserActions.LoadBookmark9,
        };

    private static readonly UserActions[] _saveBookmarkActions =
        {
            UserActions.SaveBookmark0,
            UserActions.SaveBookmark1,
            UserActions.SaveBookmark2,
            UserActions.SaveBookmark3,
            UserActions.SaveBookmark4,
            UserActions.SaveBookmark5,
            UserActions.SaveBookmark6,
            UserActions.SaveBookmark7,
            UserActions.SaveBookmark8,
            UserActions.SaveBookmark9,
        };

    private static bool DoesBookmarkExist(int index)
    {
        return GetBookmarkAt(index) != null;
    }

    private static Bookmark? GetBookmarkAt(int index)
    {
        var bookmarks = UserSettings.Config.Bookmarks;
        return bookmarks.Count > index ? bookmarks[index] : null;
    }


    private static bool HasInteractedInCurrentFrame()
    {
        var frameIndex = ImGui.GetFrameCount();
        return _lastInteractionFrame == frameIndex;
    }
        
    private static int _lastInteractionFrame;
}

// todo - include Project in this
public sealed class Bookmark
{
    // Fixme: Deserialization doesn't work and results into new (incorrect) random Ids
    //[JsonConverter(typeof(List<Guid>))]
    public List<Guid> IdPath = [];
    public CanvasScope ViewScope;

    public List<Guid> SelectedChildIds = [];
    
}