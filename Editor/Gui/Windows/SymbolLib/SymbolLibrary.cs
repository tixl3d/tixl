#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Editor.Gui.Dialogs;
using T3.Editor.Gui.Legacy.Interaction.Connections;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.SymbolLib;

/// <summary>
/// Shows a tree of all defined symbols sorted by namespace 
/// </summary>
internal sealed class SymbolLibrary : Window
{
    internal SymbolLibrary()
    {
        _filter.SearchString = "";
        _randomPromptGenerator = new RandomPromptGenerator(_filter);
        _libraryFiltering = new LibraryFiltering(this);
        Config.Title = "Symbol Library";
        _treeNode.PopulateCompleteTree();
    }

    protected override void DrawContent()
    {
        if (_subtreeNodeToRename != null)
            _renameNamespaceDialog.Draw(_subtreeNodeToRename);

        if (_symbolToDelete != null)
            _deleteSymbolDialog.Draw(_symbolToDelete);

        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 10);

        if (_symbolUsageReferenceFilter != null)
        {
            DrawUsagesAReferencedSymbol();
        }
        else
        {
            DrawView();
        }

        ImGui.PopStyleVar(1);
    }

    private static bool _refreshTriggered;

    /// <summary>
    /// Draws the main symbol library view including search, filters and result tree.
    /// </summary>
    private void DrawView()
    {
        var iconCount = 1;
        if (_wasScanned)
            iconCount++;

        CustomComponents.DrawInputFieldWithPlaceholder(
            "Search symbols...",
            ref _filter.SearchString,
            -ImGui.GetFrameHeight() * iconCount + 16);

        ImGui.SameLine();
        if (CustomComponents.IconButton(Icon.Refresh, Vector2.Zero, CustomComponents.ButtonStates.Dimmed) || _refreshTriggered)
        {
            UpdateSymbolLibraryState();
            _refreshTriggered = false;
        }

        CustomComponents.TooltipForLastItem(
            "Scan usage dependencies for symbols",
            "This can be useful for cleaning up operator name spaces.");

        if (_wasScanned)
        {
            _libraryFiltering.DrawSymbolFilters();
        }

        ImGui.BeginChild("scrolling", Vector2.Zero, false, ImGuiWindowFlags.NoBackground);
        {
            if (_libraryFiltering.AnyFilterActive)
            {
                DrawNode(FilteredTree);
            }
            else if (string.IsNullOrEmpty(_filter.SearchString))
            {
                DrawNode(_treeNode);
            }
            else if (_filter.SearchString.Contains('?'))
            {
                _randomPromptGenerator.DrawRandomPromptList();
            }
            else
            {
                DrawFilteredList();
            }
        }
        ImGui.EndChild();
    }

    private void UpdateSymbolLibraryState()
    {
        _treeNode.PopulateCompleteTree();
        ExampleSymbolLinking.UpdateExampleLinks();
        SymbolAnalysis.UpdateDetails();
        _wasScanned = true;
    }

    /// <summary>
    /// Shows usage list if a “used by” indicator was clicked for a symbol.
    /// </summary>
    private static void DrawUsagesAReferencedSymbol()
    {
        if (_symbolUsageReferenceFilter == null)
            return;

        ImGui.Text("Usages of " + _symbolUsageReferenceFilter.Name + ":");
        if (ImGui.Button("Clear"))
        {
            _symbolUsageReferenceFilter = null;
        }
        else
        {
            ImGui.Separator();

            ImGui.BeginChild("scrolling");
            {
                if (SymbolAnalysis.DetailsInitialized &&
                    SymbolAnalysis.InformationForSymbolIds.TryGetValue(_symbolUsageReferenceFilter.Id, out var info))
                {
                    // TODO: this should be cached...
                    var allSymbols = EditorSymbolPackage.AllSymbols.ToDictionary(s => s.Id);

                    foreach (var id in info.DependingSymbols)
                    {
                        if (allSymbols.TryGetValue(id, out var symbol))
                        {
                            DrawSymbolItem(symbol);
                        }
                    }
                }
            }
            ImGui.EndChild();
        }
    }

    /// <summary>
    /// Draws flat list results when search is active.
    /// </summary>
    private void DrawFilteredList()
    {
        _filter.UpdateIfNecessary(null);
        foreach (var symbolUi in _filter.MatchingSymbolUis)
        {
            DrawSymbolItem(symbolUi.Symbol);
        }
    }

    /// <summary>
    /// Recursively draws namespace nodes and their symbols.
    /// </summary>
    private void DrawNode(NamespaceTreeNode subtree)
    {
        if (subtree.Name == NamespaceTreeNode.RootNodeId)
        {
            DrawNodeItems(subtree);
        }
        else
        {
            ImGui.PushID(subtree.Name);
            ImGui.SetNextItemWidth(10);
            if (subtree.Name == "Lib" && !_openedLibFolderOnce)
            {
                ImGui.SetNextItemOpen(true);
                _openedLibFolderOnce = true;
            }

            var isOpen = ImGui.TreeNode(subtree.Name);

            CustomComponents.ContextMenuForItem(() =>
            {
                if (ImGui.MenuItem("Rename Namespace"))
                {
                    _subtreeNodeToRename = subtree;
                    _renameNamespaceDialog.ShowNextFrame();
                }
            });

            if (isOpen)
            {
                HandleDropTarget(subtree);

                DrawNodeItems(subtree);

                ImGui.TreePop();
            }
            else
            {
                // Small helper button for quickly dropping dragged symbols into unopened namespaces.
                if (DragAndDropHandling.IsDragging)
                {
                    ImGui.SameLine();
                    ImGui.PushID("DropButton");
                    ImGui.Button("  <-", new Vector2(50, 15));
                    HandleDropTarget(subtree);
                    ImGui.PopID();
                }
            }

            ImGui.PopID();
        }
    }

    /// <summary>
    /// Draws child namespaces and symbols of a subtree.
    /// </summary>
    private void DrawNodeItems(NamespaceTreeNode subtree)
    {
        // Using a for loop to prevent modification during iteration exception
        for (var index = 0; index < subtree.Children.Count; index++)
        {
            var subspace = subtree.Children[index];
            DrawNode(subspace);
        }

        // Use a copy of the list to avoid modification issues when symbols are moved/deleted.
        for (var index = 0; index < subtree.Symbols.ToList().Count; index++)
        {
            var symbol = subtree.Symbols.ToList()[index];
            DrawSymbolItem(symbol);
        }
    }

    /// <summary>
    /// Handles drag&drop onto a namespace node to move symbols between namespaces.
    /// </summary>
    private static void HandleDropTarget(NamespaceTreeNode subtree)
    {
        DragAndDropHandling.TryHandleItemDrop(DragAndDropHandling.DragTypes.Symbol, out var data, out var result);
        
        if(result != DragAndDropHandling.DragInteractionResult.Dropped)
            return;

        if (!Guid.TryParse(data, out var symbolId))
            return;

        if (!MoveSymbolToNamespace(symbolId, subtree.GetAsString(), out var reason))
            BlockingWindow.Instance.ShowMessageBox(reason, "Could not move symbol's namespace");

        _refreshTriggered = true;
    }

    /// <summary>
    /// Moves a symbol to a new namespace, respecting read-only packages.
    /// </summary>
    private static bool MoveSymbolToNamespace(Guid symbolId, string nameSpace, out string reason)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolUi))
        {
            reason = $"Could not find symbol with id '{symbolId}'";
            return false;
        }

        if (symbolUi.Symbol.Namespace == nameSpace)
        {
            reason = string.Empty;
            return true;
        }

        if (symbolUi.Symbol.SymbolPackage.IsReadOnly)
        {
            reason = $"Could not move symbol [{symbolUi.Symbol.Name}] because its package is not modifiable";
            return false;
        }

        return EditableSymbolProject.ChangeSymbolNamespace(symbolUi.Symbol, nameSpace, out reason);
    }

    internal override List<Window> GetInstances()
    {
        return [];
    }

    private bool _wasScanned;

    internal readonly NamespaceTreeNode FilteredTree = new(NamespaceTreeNode.RootNodeId);
    private NamespaceTreeNode? _subtreeNodeToRename;
    private bool _openedLibFolderOnce;

    private readonly NamespaceTreeNode _treeNode = new(NamespaceTreeNode.RootNodeId);
    private readonly SymbolFilter _filter = new();
    private static readonly RenameNamespaceDialog _renameNamespaceDialog = new();

    private static Symbol? _symbolUsageReferenceFilter;
    private readonly RandomPromptGenerator _randomPromptGenerator;
    private readonly LibraryFiltering _libraryFiltering;

    private static readonly DeleteSymbolDialog _deleteSymbolDialog = new();
    private static bool _showDeleteDialog = true;
    private static Symbol? _symbolToDelete;

    // Static back-reference so DrawSymbolItem can set _symbolToDelete
    internal static void DrawSymbolItem(Symbol symbol)
    {
        if (!symbol.TryGetSymbolUi(out var symbolUi))
            return;

        ImGui.PushID(symbol.Id.GetHashCode());
        {
            var color = symbol.OutputDefinitions.Count > 0
                            ? TypeUiRegistry.GetPropertiesForType(symbol.OutputDefinitions[0]?.ValueType).Color
                            : UiColors.Gray;

            // Tag “bookmark” button in front of symbol button.
            if (ParameterWindow.DrawSymbolTagsButton(symbolUi))
                symbolUi.FlagAsModified();

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Button, ColorVariations.OperatorBackground.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.Text, ColorVariations.OperatorLabel.Apply(color).Rgba);

            if (ImGui.Button(symbol.Name.AddSpacesForImGuiOutput()))
            {
                // (selection is handled elsewhere)
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

                if (!string.IsNullOrEmpty(symbolUi.Description))
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4, 4));
                    ImGui.BeginTooltip();
                    ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25.0f);
                    ImGui.TextUnformatted(symbolUi.Description);
                    ImGui.PopTextWrapPos();
                    ImGui.PopStyleVar();
                    ImGui.EndTooltip();
                }
            }

            ImGui.PopStyleColor(4);
            HandleDragAndDropForSymbolItem(symbol);

            // Styled context menu with symbol name as header and proper popup padding.
            CustomComponents.ContextMenuForItem(
                drawMenuItems: () =>
                {
                    // Existing symbol-specific menu
                    CustomComponents.DrawSymbolCodeContextMenuItem(symbol);

                    ImGui.Separator();

                    // Delete symbol menu entry
                    if (ImGui.MenuItem("Delete Symbol"))
                    {
                        _symbolToDelete = symbol;
                        _deleteSymbolDialog.ShowNextFrame();
                    }
                },
                title: symbol.Name,
                id: "##symbolTreeSymbolContextMenu");

            if (SymbolAnalysis.DetailsInitialized &&
                SymbolAnalysis.InformationForSymbolIds.TryGetValue(symbol.Id, out var info))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);

                ListSymbolSetWithTooltip(
                    250,
                    Icon.Dependencies,
                    "{0}",
                    string.Empty,
                    "requires...",
                    info.RequiredSymbolIds.ToList());

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ListSymbolSetWithTooltip(
                    300,
                    Icon.None,
                    "{0}",
                    string.Empty,
                    "has invalid references...",
                    info.InvalidRequiredIds);
                ImGui.PopStyleColor();

                if (ListSymbolSetWithTooltip(
                        340,
                        Icon.Referenced,
                        "{0}",
                        " NOT USED",
                        "used by...",
                        info.DependingSymbols.ToList()))
                {
                    _symbolUsageReferenceFilter = symbol;
                }

                ImGui.PopStyleColor();
            }

            if (ExampleSymbolLinking.TryGetExamples(symbol.Id, out var examples))
            {
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f * ImGui.GetStyle().Alpha);
                for (var index = 0; index < examples.Count; index++)
                {
                    var exampleSymbolUi = examples[index];
                    ImGui.SameLine();
                    ImGui.Button("EXAMPLE");
                    HandleDragAndDropForSymbolItem(exampleSymbolUi.Symbol);
                }

                ImGui.PopStyleVar();
                ImGui.PopFont();
            }
        }
        ImGui.PopID();

        // Modal delete confirmation dialog for the symbol selected via context menu.
        if (_symbolToDelete != null && ImGui.BeginPopupModal("DeleteSymbol", ref _showDeleteDialog))
        {
            _deleteSymbolDialog.Draw(_symbolToDelete);
            if (!_showDeleteDialog)  // Dialog closed
            {
                _symbolToDelete = null;
            }
            ImGui.EndPopup();
        }
    }

    /// <summary>
    /// Draws small “requires / invalid / used by” badges with tooltips.
    /// </summary>
    private static bool ListSymbolSetWithTooltip(
        float x,
        Icon icon,
        string setTitleFormat,
        string emptySetTitle,
        string toolTopTitle,
        List<Guid> symbolSet)
    {
        var activated = false;
        ImGui.PushID(icon.ToString());
        ImGui.SameLine(x, 10);
        if (symbolSet.Count > 0)
        {
            icon.DrawAtCursor();
            CustomComponents.TooltipForLastItem(DrawTooltip);
            ImGui.SameLine(0, 0);
        }

        if (symbolSet.Count == 0)
        {
            ImGui.TextUnformatted(emptySetTitle);
        }
        else
        {
            ImGui.TextUnformatted(string.Format(setTitleFormat, symbolSet.Count));
            CustomComponents.TooltipForLastItem(DrawTooltip);

            if (ImGui.IsItemClicked())
            {
                activated = true;
            }
        }

        ImGui.PopID();
        return activated;

        void DrawTooltip()
        {
            var allSymbolUis = EditorSymbolPackage.AllSymbolUis;

            var matches = allSymbolUis
                         .Where(s => symbolSet.Contains(s.Symbol.Id))
                         .OrderBy(s => s.Symbol.Namespace)
                         .ThenBy(s => s.Symbol.Name);

            ImGui.BeginTooltip();

            ImGui.TextUnformatted(toolTopTitle);
            FormInputs.AddVerticalSpace();
            ListSymbols(matches);
            ImGui.EndTooltip();
        }
    }

    /// <summary>
    /// Helper to render grouped symbol lists inside dependency tooltips.
    /// </summary>
    private static void ListSymbols(IOrderedEnumerable<SymbolUi> symbolUis)
    {
        var lastGroupName = string.Empty;
        ColumnLayout.StartLayout(25);
        foreach (var required in symbolUis)
        {
            var projectName = required.Symbol.SymbolPackage.RootNamespace;
            if (projectName != lastGroupName)
            {
                lastGroupName = projectName;
                FormInputs.AddVerticalSpace(5);
                ImGui.PushFont(Fonts.FontSmall);
                ImGui.TextUnformatted(projectName);
                ImGui.PopFont();
            }

            var hasIssues = required.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete)
                            | required.Tags.HasFlag(SymbolUi.SymbolTags.NeedsFix);
            var color = hasIssues ? UiColors.StatusAttention : UiColors.Text;
            ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
            ColumnLayout.StartGroupAndWrapIfRequired(1);
            ImGui.TextUnformatted(required.Symbol.Name);
            ColumnLayout.ExtendWidth(ImGui.GetItemRectSize().X);
            ImGui.PopStyleColor();
        }
    }

    /// <summary>
    /// Handles drag&drop source for symbol items and click-to-insert behavior.
    /// </summary>
    internal static void HandleDragAndDropForSymbolItem(Symbol symbol)
    {
        if (IsSymbolCurrentCompositionOrAParent(symbol))
            return;

        DragAndDropHandling.HandleDragSourceForLastItem(
            DragAndDropHandling.DragTypes.Symbol,
            symbol.Id.ToString(),
            "Create instance");

        if (!ImGui.IsItemDeactivated())
            return;

        var wasClick = ImGui.GetMouseDragDelta().Length() < 4;
        if (wasClick)
        {
            var components = ProjectView.Focused;
            if (components == null)
            {
                Log.Error($"No focused graph window found");
            }
            else if (components.NodeSelection.GetSelectedChildUis().Count() == 1)
            {
                ConnectionMaker.InsertSymbolInstance(components, symbol);
            }
        }
    }

    /// <summary>
    /// Prevents dragging the current composition or any of its parents into itself.
    /// </summary>
    private static bool IsSymbolCurrentCompositionOrAParent(Symbol symbol)
    {
        var components = ProjectView.Focused;
        if (components?.CompositionInstance == null)
            return false;

        var comp = components.CompositionInstance;

        if (comp.Symbol.Id == symbol.Id)
        {
            return true;
        }

        var instance = comp;
        while (instance != null)
        {
            if (instance.Symbol.Id == symbol.Id)
                return true;

            instance = instance.Parent;
        }

        return false;
    }
}
