#nullable enable
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.MagGraph.Interaction;

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

        FormInputs.AddVerticalSpace(5);

        ImGui.GetCursorPosY();

        // Find active page
        _activePage = _path[0];

        for (var index = _path.Count - 1; index >= 0; index--)
        {
            var group = _path[index];
            if (group.Variant == Variants.Page)
                _activePage = group;
        }

        var lastCurrentGroup = _path[^1];

        ColumnLayout.StartLayout();

        if (lastCurrentGroup.Variant == Variants.Page || lastCurrentGroup.Variant == Variants.Project)
        {
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
                uiResult |= PlaceHolderUi.DrawSymbolUiEntry(context, symbolUi);
                WindowContentExtend.ExtendToLastItem();
            }
        }

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
                    var color = ColorVariations.OperatorLabel.Apply(TypeUiRegistry.GetPropertiesForType(group.Type).Color);
                    ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
                    ImGui.TextUnformatted($"{group.Name}...");
                    ImGui.PopStyleColor();

                    if (ImGui.IsItemClicked())
                    {
                        _path.Clear();
                        _path.AddRange([..groupPath, group]);
                        ImGui.SetScrollY(0);
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
                        uiResult |= PlaceHolderUi.DrawSymbolUiEntry(context, symbolUi);
                        WindowContentExtend.ExtendToLastItem();
                        ColumnLayout.ExtendWidth(ImGui.GetItemRectSize().X);
                    }

                    break;
                }
                case Variants.Namespace:
                {
                    var color = ColorVariations.OperatorLabel.Apply(TypeUiRegistry.GetPropertiesForType(group.Type).Color);
                    ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
                    ImGui.TextUnformatted(group.Name + "/");
                    ImGui.PopStyleColor();
                    WindowContentExtend.ExtendToLastItem();
                    if (ImGui.IsItemClicked())
                    {
                        _path.Clear();
                        _path.AddRange([..groupPath, group]);
                        ImGui.SetScrollY(0);
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

    private static void DrawGroupHeader(Group group)
    {
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
                        new Group(Variants.Namespace, "draw", type: typeof(Texture2D)),
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
                        new Group(Variants.Namespace, "field", type: typeof(ShaderGraphNode)),
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
}