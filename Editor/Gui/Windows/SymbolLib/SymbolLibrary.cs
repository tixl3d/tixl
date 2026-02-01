#nullable enable

using ImGuiNET;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Core.Utils;
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
/// The <c>SymbolLibrary</c> window displays a hierarchical tree of all defined symbols, organized by namespace.
/// It provides search, filtering, and management features for symbols, including drag-and-drop, renaming namespaces,
/// deleting symbols, and visual feedback for selection and usage dependencies.
/// </summary>
/// <remarks>
/// This class is the main UI for browsing, searching, and managing operator symbols in the editor.
/// It supports advanced features such as dependency scanning, random prompt suggestions, and context menus for symbol actions.
/// </remarks>
internal sealed class SymbolLibrary : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SymbolLibrary"/> window.
    /// Sets up symbol filtering, random prompt generation, and filtering UI logic.
    /// Also populates the symbol tree with all available symbols.
    /// </summary>
    internal SymbolLibrary()
    {
        _filter.SearchString = "";
        _randomPromptGenerator = new RandomPromptGenerator(_filter);
        _libraryFiltering = new LibraryFiltering(this);
        Config.Title = "Symbol Library";
        _treeNode.PopulateCompleteTree();
    }

    /// <summary>
    /// Draws the main content of the Symbol Library window, including dialogs and the symbol tree or usage view.
    /// </summary>
    protected override void DrawContent()
    {
        // Update highlight/aim icon state for selected symbol
        UpdateSelectedSymbolHighlight();

        // Show rename namespace dialog if needed
        if (_subtreeNodeToRename != null)
            _renameNamespaceDialog.Draw(_subtreeNodeToRename);

        // Show delete symbol dialog if needed
        if (_symbolToDelete != null)
            _deleteSymbolDialog.Draw(_symbolToDelete);

        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, 10);

        // Show usages view if a symbol's usage is being inspected
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

    // Indicates if a refresh of the symbol library is needed
    private static bool _refreshTriggered;

    /// <summary>
    /// Draws the main symbol library view, including search bar, filters, and the result tree.
    /// </summary>
    private void DrawView()
    {
        var iconCount = 1;
        if (_wasScanned)
            iconCount++;

        // Draw search input field
        CustomComponents.DrawInputFieldWithPlaceholder(
                                                       "Search symbols...",
                                                       ref _filter.SearchString,
                                                       -ImGui.GetFrameHeight() * iconCount + 16);

        ImGui.SameLine();
        // Draw refresh button and handle refresh logic
        if (CustomComponents.IconButton(Icon.Refresh, Vector2.Zero, CustomComponents.ButtonStates.Dimmed) || _refreshTriggered)
        {
            UpdateSymbolLibraryState();
            _refreshTriggered = false;
        }

        CustomComponents.TooltipForLastItem(
                                            "Scan usage dependencies for symbols",
                                            "This can be useful for cleaning up operator name spaces.");

        // Draw filter toggles if scan was performed
        if (_wasScanned)
        {
            _libraryFiltering.DrawSymbolFilters();
        }

        var textColor = UiColors.Text.Fade(0.8f);
        ImGui.PushStyleColor(ImGuiCol.Text, textColor.Rgba);
        ImGui.BeginChild("scrolling", Vector2.Zero, false, ImGuiWindowFlags.NoBackground);
        {
            // Show filtered or full tree depending on filter/search state
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
        ImGui.PopStyleColor();
        ImGui.EndChild();
    }

    /// <summary>
    /// Updates the symbol library state by repopulating the tree and updating analysis details.
    /// </summary>
    private void UpdateSymbolLibraryState()
    {
        _treeNode.PopulateCompleteTree();
        ExampleSymbolLinking.UpdateExampleLinks();
        SymbolAnalysis.UpdateDetails();
        _wasScanned = true;
    }

    /// <summary>
    /// Shows a list of usages for a referenced symbol if the "used by" indicator was clicked.
    /// </summary>
    private void DrawUsagesAReferencedSymbol()
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
                            // Use instance method
                            this.DrawSymbolItemInstance(symbol);
                        }
                    }
                }
            }
            ImGui.EndChild();
        }
    }

    /// <summary>
    /// Draws a flat list of matching symbols when search is active.
    /// </summary>
    private void DrawFilteredList()
    {
        _filter.UpdateIfNecessary(null);
        foreach (var symbolUi in _filter.MatchingSymbolUis)
        {
            this.DrawSymbolItemInstance(symbolUi.Symbol);
        }
    }

    // --- Expand-to-symbol logic ---
    // Indicates if the tree should expand to reveal a specific symbol
    private bool _expandToSymbolTriggered;

    // The target symbol ID to expand to
    private Guid? _expandToSymbolTargetId;

    /// <summary>
    /// Checks if a <see cref="NamespaceTreeNode"/> is in the path to a symbol, returning the path if found.
    /// </summary>
    private static bool IsInPathToSymbol(NamespaceTreeNode node, Guid symbolId, out List<NamespaceTreeNode> path)
    {
        path = new List<NamespaceTreeNode>();
        return FindPathRecursive(node, symbolId, path);
    }

    /// <summary>
    /// Recursively searches for a symbol in the tree and builds the path to it.
    /// </summary>
    private static bool FindPathRecursive(NamespaceTreeNode node, Guid symbolId, List<NamespaceTreeNode> path)
    {
        if (node.Symbols.Any(s => s.Id == symbolId))
        {
            path.Add(node);
            return true;
        }

        foreach (var child in node.Children)
        {
            if (FindPathRecursive(child, symbolId, path))
            {
                path.Add(node);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recursively draws namespace nodes and their symbols in the tree view.
    /// </summary>
    /// <param name="subtree">The subtree node to draw.</param>
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

            // --- Aim icon logic for tree nodes ---
            var selectedSymbolId = _lastSelectedSymbolId;
            var containsSelected = selectedSymbolId != null && ContainsSymbolRecursive(subtree, selectedSymbolId.Value);

            // Expand all nodes in the path to the target symbol if triggered
            if (_expandToSymbolTriggered && _expandToSymbolTargetId.HasValue)
            {
                if (IsInPathToSymbol(subtree, _expandToSymbolTargetId.Value, out var path) && path.Contains(subtree))
                {
                    ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                }
            }

            var isOpen = ImGui.TreeNode(subtree.Name);

            // Draw aim icon if this node contains the selected symbol and is not open
            if (!isOpen && containsSelected)
            {
                var h = ImGui.GetFontSize();
                var x = ImGui.GetContentRegionMax().X - h;
                ImGui.SameLine(x);

                var clicked = ImGui.InvisibleButton("Reveal", new Vector2(h));
                if (ImGui.IsItemHovered())
                {
                    CustomComponents.TooltipForLastItem("Reveal selected operator");
                }

                // Animate aim icon
                var timeSinceChange = (float)(ImGui.GetTime() - _lastSelectionTime);
                var fadeProgress = Clamp01(timeSinceChange / 0.5f);
                var blinkFade = -MathF.Cos(timeSinceChange * 15f) * (1f - fadeProgress) * 0.7f + 0.75f;
                var color = UiColors.StatusActivated.Fade(blinkFade);
                Icons.DrawIconOnLastItem(Icon.Aim, color);

                // Optionally, scroll to item if just selected
                if (_expandToSymbolTriggered && selectedSymbolId.HasValue)
                {
                    ImGui.SetScrollHereY();
                }

                // Set expand trigger if clicked
                if (clicked && selectedSymbolId.HasValue)
                {
                    _expandToSymbolTriggered = true;
                    _expandToSymbolTargetId = selectedSymbolId;
                }
            }

            // Context menu for namespace node
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
                // Reset expand trigger after expanding and target is visible
                if (_expandToSymbolTriggered && _expandToSymbolTargetId.HasValue && ContainsSymbolRecursive(subtree, _expandToSymbolTargetId.Value))
                {
                    // If this is the last node in the path, reset
                    if (subtree.Symbols.Any(s => s.Id == _expandToSymbolTargetId.Value))
                    {
                        _expandToSymbolTriggered = false;
                        _expandToSymbolTargetId = null;
                    }
                }

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
    /// Checks if a <see cref="NamespaceTreeNode"/> contains a symbol with the given ID, recursively.
    /// </summary>
    private static bool ContainsSymbolRecursive(NamespaceTreeNode node, Guid symbolId)
    {
        if (node.Symbols.Any(s => s.Id == symbolId))
            return true;
        foreach (var child in node.Children)
        {
            if (ContainsSymbolRecursive(child, symbolId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Draws all child namespaces and symbols of a subtree node.
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
            this.DrawSymbolItemInstance(symbol);
        }
    }

    /// <summary>
    /// Handles drag-and-drop onto a namespace node to move symbols between namespaces.
    /// </summary>
    private static void HandleDropTarget(NamespaceTreeNode subtree)
    {
        var result = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.Symbol, out var data);

        if (result != DragAndDropHandling.DragInteractionResult.Dropped)
            return;

        if (!Guid.TryParse(data, out var symbolId))
            return;

        if (!MoveSymbolToNamespace(symbolId, subtree.GetAsString(), out var reason))
            BlockingWindow.Instance.ShowMessageBox(reason, "Could not move symbol's namespace");

        _refreshTriggered = true;
    }

    /// <summary>
    /// Moves a symbol to a new namespace, respecting read-only package restrictions.
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

    /// <summary>
    /// Returns an empty list, as only one instance of SymbolLibrary is supported.
    /// </summary>
    internal override List<Window> GetInstances()
    {
        return [];
    }

    // --- State fields ---
    // Indicates if the library was scanned for dependencies
    private bool _wasScanned;

    /// <summary>
    /// The filtered tree of namespaces and symbols, updated by filters.
    /// </summary>
    internal readonly NamespaceTreeNode FilteredTree = new(NamespaceTreeNode.RootNodeId);

    // The namespace node currently being renamed
    private NamespaceTreeNode? _subtreeNodeToRename;

    // Tracks if the Lib folder was opened once
    private bool _openedLibFolderOnce;

    // The root node of the full symbol tree
    private readonly NamespaceTreeNode _treeNode = new(NamespaceTreeNode.RootNodeId);

    // The symbol filter for search and matching
    private readonly SymbolFilter _filter = new();

    // Dialog for renaming namespaces
    private static readonly RenameNamespaceDialog _renameNamespaceDialog = new();

    // The symbol currently being inspected for usages
    private static Symbol? _symbolUsageReferenceFilter;

    // Generator for random prompt suggestions
    private readonly RandomPromptGenerator _randomPromptGenerator;

    // Filtering UI and logic for the library
    private readonly LibraryFiltering _libraryFiltering;

    // Dialog for deleting symbols
    private static readonly DeleteSymbolDialog _deleteSymbolDialog = new();

    // Controls visibility of the delete dialog
    private static bool _showDeleteDialog = true;

    // The symbol currently selected for deletion
    private static Symbol? _symbolToDelete;

    // --- Highlight and Aim Icon for selected operator in node graph ---
    // Store the last selected symbol id and time for highlight/aim icon animation
    private static Guid? _lastSelectedSymbolId;
    private static double _lastSelectionTime;

    /// <summary>
    /// Updates the highlight/aim icon state for the currently selected symbol in the node graph.
    /// </summary>
    private void UpdateSelectedSymbolHighlight()
    {
        var projectView = ProjectView.Focused;
        if (projectView?.NodeSelection == null)
            return;

        // Only highlight if exactly one operator is selected in the node graph
        var selectedChildUis = projectView.NodeSelection.GetSelectedChildUis().ToList();
        if (selectedChildUis.Count == 1)
        {
            var symbolChild = selectedChildUis[0].SymbolChild;
            if (symbolChild == null)
                return;

            var selectedSymbolId = symbolChild.Symbol.Id;
            if (_lastSelectedSymbolId != selectedSymbolId)
            {
                _lastSelectedSymbolId = selectedSymbolId;
                _lastSelectionTime = ImGui.GetTime();
            }
        }
        else
        {
            _lastSelectedSymbolId = null;
        }
    }

    // Helper for clamping float/double values between 0 and 1
    private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    private static float Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : (float)v;

    /// <summary>
    /// Static wrapper for drawing a symbol item, used for external static calls.
    /// </summary>
    internal static void DrawSymbolItemStatic(Symbol symbol)
    {
        // Use WindowManager to get all windows and find the first SymbolLibrary instance
        var symbolLibraryInstance = T3.Editor.Gui.Windows.Layouts.WindowManager.GetAllWindows().OfType<SymbolLibrary>().FirstOrDefault();
        symbolLibraryInstance?.DrawSymbolItemInstance(symbol);
    }

    /// <summary>
    /// Static method for legacy external calls.
    /// </summary>
    internal static void DrawSymbolItem(Symbol symbol)
    {
        DrawSymbolItemStatic(symbol);
    }

    /// <summary>
    /// Instance method for drawing a symbol item in the tree, including highlight, context menu, and dependency badges.
    /// </summary>
    /// <param name="symbol">The symbol to draw.</param>
    internal void DrawSymbolItemInstance(Symbol symbol)
    {
        if (!symbol.TryGetSymbolUi(out var symbolUi))
            return;

        ImGui.PushID(symbol.Id.GetHashCode());
        {
            var color = symbol.OutputDefinitions.Count > 0
                            ? TypeUiRegistry.GetPropertiesForType(symbol.OutputDefinitions[0]?.ValueType).Color
                            : UiColors.Gray;

            // --- Highlight and Aim Icon for selected symbol ---
            var isSelected = false;
            var timeSinceSelection = 0f;
            if (_lastSelectedSymbolId.HasValue && symbol.Id == _lastSelectedSymbolId.Value)
            {
                isSelected = true;
                timeSinceSelection = (float)(ImGui.GetTime() - _lastSelectionTime);
            }

            // Tag “bookmark” button in front of symbol button.
            if (ParameterWindow.DrawSymbolTagsButton(symbolUi))
                symbolUi.FlagAsModified();

            ImGui.SameLine();

            ImGui.PushStyleColor(ImGuiCol.Button, ColorVariations.OperatorBackground.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.Text, ColorVariations.OperatorLabel.Apply(color).Rgba);

            bool buttonPressed = ImGui.Button(symbol.Name.AddSpacesForImGuiOutput());

            // Get button rect for icon and highlight
            var buttonMin = ImGui.GetItemRectMin();
            var buttonMax = ImGui.GetItemRectMax();

            // Draw highlight border if selected (drawn last, on top)
            if (isSelected)
            {
                var fadeProgress = Clamp01(timeSinceSelection / 0.3f);
                var blinkFade = MathUtils.Lerp(-MathF.Cos(timeSinceSelection * 15f) * 0.8f + 0.2f, 1, fadeProgress);

                var highlightColor = UiColors.StatusActivated.Fade(blinkFade);
                ImGui.GetWindowDrawList().AddRect(buttonMin, buttonMax, highlightColor, 5);
            }

            // Show tooltip with description if hovered
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

            // Draw dependency badges if analysis is available
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

            // Draw example badges if available
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
            if (!_showDeleteDialog) // Dialog closed
            {
                _symbolToDelete = null;
            }

            ImGui.EndPopup();
        }
    }

    /// <summary>
    /// Draws small badges for symbol dependencies, invalid references, or usages, with tooltips.
    /// </summary>
    private static bool ListSymbolSetWithTooltip(float x,
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

        // Tooltip callback to show detailed symbol list
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
    /// Handles drag-and-drop source for symbol items and click-to-insert behavior.
    /// </summary>
    internal static void HandleDragAndDropForSymbolItem(Symbol symbol)
    {
        if (IsSymbolCurrentCompositionOrAParent(symbol))
            return;

        DragAndDropHandling.HandleDragSourceForLastItem(
                                                        DragAndDropHandling.DragTypes.Symbol,
                                                        symbol.Id.ToString());

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