#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Bookmarks operators across a project on the digit keys: Shift+digit bookmarks the selected operator (again to
/// remove it), the digit selects it and brings it into view, opening its composition if needed. Bookmarked
/// operators show their digit as a small badge in the graph.
/// </summary>
internal static class OperatorBookmarks
{
    internal static void HandleKeyboard(GraphUiContext context)
    {
        for (var index = 0; index < SlotCount; index++)
        {
            if ((UserActions.SetOperatorBookmark0 + index).Triggered())
            {
                ToggleBookmark(context, index);
                return;
            }

            if ((UserActions.SelectOperatorBookmark0 + index).Triggered())
            {
                JumpToBookmark(context, index);
                return;
            }
        }
    }

    /** Draws the digit of each bookmarked operator of the current composition in a circle at its top left corner. */
    internal static void DrawBadges(GraphUiContext context, ImDrawListPtr drawList)
    {
        var paths = GetPaths(context);
        var compositionPath = context.CompositionInstance.InstancePath;
        var scale = T3Ui.UiScaleFactor;
        ImGui.PushFont(Fonts.FontSmall);
        var lineHeight = ImGui.GetTextLineHeight();
        for (var index = 0; index < SlotCount; index++)
        {
            var path = paths[index];
            if (path == null || !IsChildPathOf(path, compositionPath)
                             || !context.Layout.Items.TryGetValue(path[^1], out var item)
                             || item.IsCollapsedAway)
                continue;

            // A circle reaching a quarter of its diameter beyond the operator's top left corner
            var area = context.View.TransformRect(item.Area);
            var label = _digitLabels[index];
            var labelSize = ImGui.CalcTextSize(label);
            var diameter = MathF.Max(labelSize.X + 2 * BadgePaddingPx * scale, lineHeight);
            var center = area.Min + new Vector2(diameter * (0.5f - BadgeOutsideFraction));
            drawList.AddCircleFilled(center, diameter / 2, UiColors.StatusActivated);
            drawList.AddText(new Vector2(MathF.Round(center.X - labelSize.X / 2), MathF.Round(center.Y - labelSize.Y / 2)),
                             UiColors.ForegroundFull, label);
        }

        ImGui.PopFont();
    }

    private static void ToggleBookmark(GraphUiContext context, int index)
    {
        var selection = context.Selector.Selection;
        if (selection.Count == 0
            || !context.Layout.Items.TryGetValue(selection[^1].Id, out var item)
            || item.Variant != Model.MagGraphItem.Variants.Operator
            || item.Instance == null)
        {
            ActionToast.Show("{Select an operator to bookmark it on " + _digitLabels[index] + "}");
            return;
        }

        var bookmarks = GetBookmarksForProject(context);
        var path = string.Join('/', item.Instance.InstancePath);
        if (bookmarks[index] == path)
        {
            bookmarks[index] = null;
            ActionToast.Show("{Removed bookmark " + _digitLabels[index] + " from " + item.ReadableName + "}");
        }
        else
        {
            // An operator has one bookmark, so setting it again moves it to the new digit
            for (var otherIndex = 0; otherIndex < SlotCount; otherIndex++)
            {
                if (bookmarks[otherIndex] == path)
                    bookmarks[otherIndex] = null;
            }

            bookmarks[index] = path;
            ActionToast.MarkLearned(EmptyBookmarkTipId);
            ActionToast.Show("{Bookmarked " + item.ReadableName + " on " + _digitLabels[index] + "}   [" + _digitLabels[index] + "] jumps to it");
        }

        _pathsProjectId = Guid.Empty;
        UserSettings.Save();
    }

    private static void JumpToBookmark(GraphUiContext context, int index)
    {
        var path = GetPaths(context)[index];
        if (path == null)
        {
            ActionToast.ShowTip(EmptyBookmarkTipId,
                                "{No operator bookmarked on " + _digitLabels[index] + "}   [⇧" + _digitLabels[index] + "] bookmarks the selected one");
            return;
        }

        var instance = context.ProjectView.Structure.GetInstanceFromIdPath(path);
        if (instance?.Parent == null)
        {
            Log.Warning($"The operator bookmarked on {_digitLabels[index]} no longer exists");
            return;
        }

        context.ProjectView.TrySetCompositionOp(instance.Parent.InstancePath, ScalableCanvas.Transition.JumpIn, instance.SymbolChildId);
    }

    private static string?[] GetBookmarksForProject(GraphUiContext context)
    {
        var projectId = context.ProjectView.RootInstance.Symbol.Id;
        if (!UserSettings.Config.OperatorBookmarks.TryGetValue(projectId, out var bookmarks) || bookmarks.Length != SlotCount)
        {
            var resized = new string?[SlotCount];
            bookmarks?.AsSpan(0, Math.Min(bookmarks.Length, SlotCount)).CopyTo(resized);
            bookmarks = resized;
            UserSettings.Config.OperatorBookmarks[projectId] = bookmarks;
        }

        return bookmarks;
    }

    /** The stored paths parsed once per project, so drawing the badges doesn't parse text every frame */
    private static Guid[]?[] GetPaths(GraphUiContext context)
    {
        var projectId = context.ProjectView.RootInstance.Symbol.Id;
        if (projectId == _pathsProjectId)
            return _paths;

        _pathsProjectId = projectId;
        Array.Clear(_paths);
        if (!UserSettings.Config.OperatorBookmarks.TryGetValue(projectId, out var bookmarks))
            return _paths;

        for (var index = 0; index < SlotCount && index < bookmarks.Length; index++)
        {
            _paths[index] = TryParsePath(bookmarks[index], out var path) ? path : null;
        }

        return _paths;
    }

    private static bool TryParsePath(string? text, out Guid[] path)
    {
        path = [];
        if (string.IsNullOrEmpty(text))
            return false;

        var parts = text.Split('/');
        path = new Guid[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!Guid.TryParse(parts[index], out path[index]))
                return false;
        }

        return true;
    }

    private static bool IsChildPathOf(Guid[] path, IReadOnlyList<Guid> compositionPath)
    {
        if (path.Length != compositionPath.Count + 1)
            return false;

        for (var index = 0; index < compositionPath.Count; index++)
        {
            if (path[index] != compositionPath[index])
                return false;
        }

        return true;
    }

    private const string EmptyBookmarkTipId = "EmptyOperatorBookmark";
    private const int SlotCount = 10;
    private const float BadgePaddingPx = 2.5f;

    /** Share of the badge's diameter that reaches beyond the operator's edges */
    private const float BadgeOutsideFraction = 0.25f;

    private static readonly string[] _digitLabels = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"];
    private static readonly Guid[]?[] _paths = new Guid[]?[SlotCount];
    private static Guid _pathsProjectId;
}
