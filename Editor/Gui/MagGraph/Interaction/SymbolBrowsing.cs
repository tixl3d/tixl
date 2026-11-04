#nullable enable
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.App;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.SystemUi;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Draws a curated overview of essential symbols structured by categories and namespaces. Besides the mouse it can
/// be browsed with the arrow keys: Return opens the focused namespace or inserts the focused symbol, Backspace goes
/// back up.
/// </summary>
internal static class SymbolBrowsing
{
    internal static PlaceHolderUi.UiResults Draw(GraphUiContext context)
    {
        if (ProjectView.Focused == null)
            return PlaceHolderUi.UiResults.None;

        var uiResult = PlaceHolderUi.UiResults.None;

        _libTree = UpdateLibPage(); // only required for hot code reloading

        if (_path.Count == 0)
            return PlaceHolderUi.UiResults.Cancel;

        _focus.BeginFrame();
        var activateFocused = HandleKeys();

        
        // Find active page
        _activePage = _path[0];

        for (var index = _path.Count - 1; index >= 0; index--)
        {
            var group = _path[index];
            if (group.Variant == Variants.Page)
                _activePage = group;
        }

        var lastCurrentGroup = _path[^1];

        // Draw tip
        if (lastCurrentGroup.Variant == Variants.Project)
        {
            Icon.Tip.DrawAtCursor(UiColors.TextMuted);
            ImGui.SameLine(0, 5);
            CustomComponents.HelpText("Explore essential ops below...");
        }
        
        ColumnLayout.StartLayout();
        ImGui.Indent(5);

        if (lastCurrentGroup.Variant == Variants.Page || lastCurrentGroup.Variant == Variants.Project)
        {
            _libTree = UpdateLibPage();
            DrawItemTree(_path, _activePage);
        }
        else
        {
            var matchingSymbolUis = new List<SymbolUi>();

            var activeNamespace = ActiveNamespaceString; // Outside of loop to away recomputing
            foreach (var symbolUi in EditorSymbolPackage.AllSymbolUis)
            {
                if (symbolUi.Symbol.Namespace == activeNamespace)
                    matchingSymbolUis.Add(symbolUi);
            }

            var orderedEnumerable = matchingSymbolUis
                .OrderByDescending(sui => SymbolFilter.ComputeRelevancy(sui,
                    string.Empty,
                    ProjectView.Focused.OpenedProject.Package,
                    context.CompositionInstance))
                .ToList();
            foreach (var symbolUi in orderedEnumerable)
            {
                uiResult |= PlaceHolderUi.DrawSymbolUiEntry(context, symbolUi, false);
                RegisterSymbolEntry(symbolUi);
                WindowContentExtend.ExtendToLastItem();
            }
        }

        ApplyRequestedPath();
        _groupToFocusAfterReturn = null;
        return uiResult;

        void DrawItemTree(List<Group> groupPath, Group group, int levelOnPage = 0)
        {
            switch (group.Variant)
            {
                case Variants.Project:
                {
                    foreach (var subItem in group.Items)
                    {
                        DrawItemTree([group], subItem, levelOnPage + 1);
                    }

                    break;
                }

                case Variants.Page when levelOnPage == 0:
                {
                    foreach (var subItem in group.Items)
                    {
                        DrawItemTree([..groupPath], subItem, levelOnPage + 1);
                    }

                    break;
                }
                case Variants.Page:
                {
                    var color = ColorVariations.OperatorLabel.Apply(TypeUiRegistry.GetPropertiesForType(group.Type)
                        .Color);
                    ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
                    ImGui.TextUnformatted($"{group.Name}...");
                    ImGui.PopStyleColor();
                    var isPageFocused = RegisterGroupEntry(group);

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.GetWindowDrawList().AddRect(
                            ImGui.GetItemRectMin() - new Vector2(3, 1),
                            ImGui.GetItemRectMax() + new Vector2(3, 1),
                            UiColors.ForegroundFull.Fade(0.3f), 3);
                    }

                    var openPageWithKey = isPageFocused && activateFocused;
                    if (ImGui.IsItemClicked() || openPageWithKey)
                    {
                        _requestedPath = [..groupPath, group];
                        _isRequestedWithKeys = openPageWithKey;
                    }

                    break;
                }
                case Variants.Namespace when levelOnPage == 1:
                {
                    var groupNamespace = GetNamespaceString([..groupPath, group]);

                    var matchingSymbolUis = new List<SymbolUi>();
                    foreach (var s in EditorSymbolPackage.AllSymbolUis)
                    {
                        if (s.Symbol.Namespace == groupNamespace && s.Tags.HasFlag(SymbolUi.SymbolTags.Essential))
                            matchingSymbolUis.Add(s);
                    }

                    if (matchingSymbolUis.Count == 0)
                        break;

                    var orderedEnumerable = matchingSymbolUis
                        .OrderByDescending(sui => SymbolFilter.ComputeRelevancy(sui,
                            string.Empty,
                            ProjectView.Focused!.OpenedProject.Package,
                            context.CompositionInstance))
                        .ToList();

                    ColumnLayout.StartGroupAndWrapIfRequired(orderedEnumerable.Count + 1);

                    DrawGroupHeader(group);
                    foreach (var symbolUi in orderedEnumerable)
                    {
                        ImGui.SetNextItemWidth(100);
                        uiResult |= PlaceHolderUi.DrawSymbolUiEntry(context, symbolUi, false);
                        RegisterSymbolEntry(symbolUi);
                        WindowContentExtend.ExtendToLastItem();
                        ColumnLayout.ExtendWidth(ImGui.GetItemRectSize().X);
                    }

                    break;
                }
                case Variants.Namespace:
                {
                    var color = ColorVariations.OperatorLabel.Apply(TypeUiRegistry.GetPropertiesForType(group.Type)
                        .Color);
                    ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
                    ImGui.TextUnformatted(group.Name + "/");
                    ImGui.PopStyleColor();
                    var isNamespaceFocused = RegisterGroupEntry(group);
                    WindowContentExtend.ExtendToLastItem();

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.GetWindowDrawList().AddRect(
                            ImGui.GetItemRectMin() - new Vector2(3, 1),
                            ImGui.GetItemRectMax() + new Vector2(3, 1),
                            UiColors.ForegroundFull.Fade(0.3f), 3);
                    }

                    var openNamespaceWithKey = isNamespaceFocused && activateFocused;
                    if (ImGui.IsItemClicked() || openNamespaceWithKey)
                    {
                        _requestedPath = [..groupPath, group];
                        _isRequestedWithKeys = openNamespaceWithKey;
                    }

                    break;
                }

                case Variants.Grouping:
                case Variants.NamespaceCategory:
                {
                    ColumnLayout.StartGroupAndWrapIfRequired(group.Items.Count + 1);
                    DrawGroupHeader(group);
                    foreach (var subItem in group.Items)
                    {
                        DrawItemTree([..groupPath, group], subItem, levelOnPage + 1);
                        WindowContentExtend.ExtendToLastItem();
                        ColumnLayout.ExtendWidth(ImGui.GetItemRectSize().X);
                    }

                    break;
                }
            }
        }
    }

    public static void Reset()
    {
        _path = [_libTree];
        _requestedPath = null;
        _groupToFocusAfterReturn = null;
        _lastDrawnFrame = NotDrawn;
        _focus.Reset();
    }

    public static bool IsFilterActive => _path.Count > 1;

    public static string FilterString => _path.Count <= 1
        ? string.Empty
        : string.Join(".", _path[1..].Select(t => t.Name));

    private static string GetNamespaceString(List<Group> groupPath)
    {
        _stringBuilder.Clear();
        foreach (var g in groupPath)
        {
            if (g.Variant == Variants.Grouping)
                continue;

            if (_stringBuilder.Length > 0)
                _stringBuilder.Append(".");

            _stringBuilder.Append(g.Name);
        }

        return _stringBuilder.ToString();
    }

    /// <returns>True if Return should open or insert the focused entry</returns>
    private static bool HandleKeys()
    {
        // The Return or Backspace that opened the browser or emptied its search must not also act in here
        var frameCount = ImGui.GetFrameCount();
        var wasDrawnLastFrame = _lastDrawnFrame == frameCount - 1;
        _lastDrawnFrame = frameCount;
        if (!wasDrawnLastFrame)
            return false;

        if (ImGui.IsKeyPressed(Key.CursorUp.ToImGuiKey()))
        {
            _focus.Move(SelectionNavigation.Directions.Up);
        }
        else if (ImGui.IsKeyPressed(Key.CursorDown.ToImGuiKey()))
        {
            _focus.Move(SelectionNavigation.Directions.Down);
        }
        else if (ImGui.IsKeyPressed(Key.CursorLeft.ToImGuiKey()))
        {
            _focus.Move(SelectionNavigation.Directions.Left);
        }
        else if (ImGui.IsKeyPressed(Key.CursorRight.ToImGuiKey()))
        {
            _focus.Move(SelectionNavigation.Directions.Right);
        }

        if (ImGui.IsKeyPressed(Key.Backspace.ToImGuiKey(), false) && _path.Count > 1)
        {
            GoUp();
            return false;
        }

        return _focus.HasFocus && ImGui.IsKeyPressed(Key.Return.ToImGuiKey(), false);
    }

    /** Steps back to the previous level that has its own view; categories and groupings are only headers. */
    private static void GoUp()
    {
        _groupToFocusAfterReturn = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        while (_path.Count > 1 && _path[^1].Variant is Variants.NamespaceCategory or Variants.Grouping)
        {
            _path.RemoveAt(_path.Count - 1);
        }

        _focus.Reset();
        ImGui.SetScrollY(0);

        if (_path.Count == 1)
            PlaceHolderUi.FocusSearchInputNextFrame();
    }

    /** Changing the path while drawing would let the rest of the old view react to the same click or key. */
    private static void ApplyRequestedPath()
    {
        if (_requestedPath == null)
            return;

        _path = _requestedPath;
        _requestedPath = null;
        ImGui.SetScrollY(0);

        _focus.Reset();
        if (_isRequestedWithKeys)
            _focus.FocusFirst();
    }

    /// <returns>True if the entry has the keyboard focus</returns>
    private static bool RegisterGroupEntry(Group group)
    {
        var index = _focus.Register(out var isFocused);

        // The tree is rebuilt every frame, so the group left with Backspace is found again by its content
        var returnTarget = _groupToFocusAfterReturn;
        if (returnTarget != null
            && returnTarget.Variant == group.Variant
            && returnTarget.Type == group.Type
            && returnTarget.Name == group.Name)
        {
            _focus.Focus(index);
            _groupToFocusAfterReturn = null;
        }

        if (isFocused)
            PlaceHolderUi.SelectSymbolFromBrowsing(null);

        return isFocused;
    }

    private static void RegisterSymbolEntry(SymbolUi symbolUi)
    {
        _focus.Register(out var isFocused);
        if (isFocused)
            PlaceHolderUi.SelectSymbolFromBrowsing(symbolUi);
    }

    private static void DrawGroupHeader(Group group)
    {
        ImGui.SetCursorPosY(ImGui.GetCursorPosY()+5);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(group.Name + "...");
        ImGui.PopStyleColor();
        ImGui.PopFont();

        WindowContentExtend.ExtendToLastItem();
    }

    private static Group UpdateLibPage()
    {
        return new Group(SymbolBrowsing.Variants.Project, "Lib",
        [
            new Group(Variants.NamespaceCategory, "numbers", [
                new Group(Variants.Page, "anim", [
                    new Group(Variants.Namespace, "time"),
                    new Group(Variants.Namespace, "animators"),
                    new Group(Variants.Namespace, "vj"),
                ]),
                new Group(Variants.Page, "float", [
                    new Group(Variants.Namespace, "basic"),
                    new Group(Variants.Namespace, "trigonometry"),
                    new Group(Variants.Namespace, "adjust"),
                    new Group(Variants.Namespace, "process"),
                    new Group(Variants.Namespace, "logic"),
                    new Group(Variants.Namespace, "random"),
                ]),
                new Group(Variants.Namespace, "vec2"),
                new Group(Variants.Namespace, "vec3"),
                new Group(Variants.Namespace, "color"),
                new Group(Variants.Namespace, "bool"),
                new Group(Variants.Page, "int", [
                    new Group(Variants.Namespace, "basic"),
                    new Group(Variants.Namespace, "logic"),
                    new Group(Variants.Namespace, "process"),
                ])
            ]),
            new Group(Variants.NamespaceCategory, "image", [
                new Group(Variants.Page, "generate", [
                    new Group(Variants.Namespace, "load", type: typeof(Texture2D)),
                    new Group(Variants.Namespace, "basic", type: typeof(Texture2D)),
                    new Group(Variants.Namespace, "noise", type: typeof(Texture2D)),
                    new Group(Variants.Namespace, "pattern", type: typeof(Texture2D)),
                ], type: typeof(Texture2D)),
                new Group(Variants.Page, "fx", [
                    new Group(Variants.Namespace, "blur", type: typeof(Texture2D)),
                    new Group(Variants.Namespace, "distort", type: typeof(Texture2D)),
                    new Group(Variants.Namespace, "stylize", type: typeof(Texture2D)),
                    new Group(Variants.Namespace, "glitch", type: typeof(Texture2D)),
                ], type: typeof(Texture2D)),
                new Group(Variants.Namespace, "color", type: typeof(Texture2D)),
                new Group(Variants.Namespace, "transform", type: typeof(Texture2D)),
                new Group(Variants.Namespace, "analyze", type: typeof(Texture2D)),
                new Group(Variants.Namespace, "use", type: typeof(Texture2D)),
            ]),
            new Group(Variants.NamespaceCategory, "point", [
                new Group(Variants.Namespace, "generate", type: typeof(BufferWithViews)),
                new Group(Variants.Namespace, "transform", type: typeof(BufferWithViews)),
                new Group(Variants.Namespace, "modify", type: typeof(BufferWithViews)),
                new Group(Variants.Namespace, "draw", type: typeof(Command)),
            ]),

            new Group(Variants.Grouping, "particles", [
                new Group(Variants.Namespace, "particle", type: typeof(BufferWithViews)),
                new Group(Variants.Page, "particle", [
                    new Group(Variants.Namespace, "force", type: typeof(BufferWithViews)),
                ], type: typeof(BufferWithViews)),
            ]),
            new Group(Variants.NamespaceCategory, "render", [
                new Group(Variants.Namespace, "basic", type: typeof(Command)),
                new Group(Variants.Namespace, "camera", type: typeof(Command)),
                new Group(Variants.Namespace, "transform", type: typeof(Command)),
                new Group(Variants.Namespace, "shading", type: typeof(Command)),
                new Group(Variants.Namespace, "postfx", type: typeof(Texture2D)),
                new Group(Variants.Namespace, "gizmo", type: typeof(Command)),
                new Group(Variants.Namespace, "utils", type: typeof(Command)),
            ]),

            new Group(Variants.Grouping, "misc", [
                new Group(Variants.Page, "field", [
                    //new Group(Variants.NamespaceCategory, "field", [
                    new Group(Variants.Namespace, "adjust", type: typeof(ShaderGraphNode)),
                    new Group(Variants.Namespace, "analyze", type: typeof(ShaderGraphNode)),
                    new Group(Variants.Namespace, "combine", type: typeof(ShaderGraphNode)),
                    new Group(Variants.Namespace, "generate.sdf", type: typeof(ShaderGraphNode)),
                    new Group(Variants.Namespace, "generate.texture", type: typeof(ShaderGraphNode)),
                    new Group(Variants.Namespace, "generate.vec3", type: typeof(ShaderGraphNode)),
                    new Group(Variants.Namespace, "render", type: typeof(ShaderGraphNode)),
                    new Group(Variants.Namespace, "space", type: typeof(ShaderGraphNode)),
                    new Group(Variants.Namespace, "use", type: typeof(ShaderGraphNode)),
                ], type: typeof(ShaderGraphNode)),
                new Group(Variants.Page, "mesh", [
                    new Group(Variants.Namespace, "generate", type: typeof(MeshBuffers)),
                    new Group(Variants.Namespace, "modify", type: typeof(MeshBuffers)),
                    new Group(Variants.Namespace, "draw", type: typeof(Command)),
                ]),
                new Group(Variants.Page, "string", [
                    new Group(Variants.Namespace, "combine", type: typeof(string)),
                    new Group(Variants.Namespace, "convert", type: typeof(string)),
                    new Group(Variants.Namespace, "search", type: typeof(string)),
                    new Group(Variants.Namespace, "logic", type: typeof(string)),
                    new Group(Variants.Namespace, "datetime", type: typeof(string)),
                    new Group(Variants.Namespace, "list", type: typeof(string)),
                    new Group(Variants.Namespace, "buffers", type: typeof(string)),
                ], type: typeof(string)),
                new Group(Variants.Page, "io", [
                    new Group(Variants.Namespace, "audio"),
                    new Group(Variants.Namespace, "file"),
                    new Group(Variants.Namespace, "input"),
                    new Group(Variants.Namespace, "midi"),
                    new Group(Variants.Namespace, "osc"),
                    new Group(Variants.Namespace, "video"),
                ]),
                new Group(Variants.Namespace, "flow", type: typeof(Command)),
            ]),
        ]);
    }

    private sealed class Group
    {
        internal Group(SymbolBrowsing.Variants variant, string name, List<Group>? items = null, Type? type = null)
        {
            Variant = variant;
            Name = name;
            Items = items ?? [];
            Type = type;
        }

        internal readonly Variants Variant;
        internal readonly string Name;
        internal readonly List<Group> Items;
        internal readonly Type? Type;

        public override string ToString()
        {
            return $"{Variant} {Name} {Items.Count}";
        }
    }

    private enum Variants
    {
        Project,
        Page,
        NamespaceCategory,
        Namespace,
        Grouping,
    }

    private static Group _libTree = UpdateLibPage();
    private static Group? _activePage;
    private static string ActiveNamespaceString => GetNamespaceString(_path);
    private static readonly StringBuilder _stringBuilder = new();
    private static List<Group> _path = [];
    private static List<Group>? _requestedPath;
    private static bool _isRequestedWithKeys;
    private static Group? _groupToFocusAfterReturn;

    private const int NotDrawn = -2;
    private static int _lastDrawnFrame = NotDrawn;
    private static readonly FocusNavigator _focus = new();
}