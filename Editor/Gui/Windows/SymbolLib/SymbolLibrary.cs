#nullable enable

using System.IO;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Dialogs;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Legacy.Interaction.Connections;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.UiHelpers.Thumbnails;
using T3.Editor.Gui.Windows.AssetLib;
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
[HelpUiID("SymbolLibrary")]
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
    /// Opens the window, expands the namespace tree to <paramref name="symbolId"/>, and scrolls it into
    /// view (without touching the search filter). Used by operator links in markdown.
    /// </summary>
    internal void Reveal(Guid symbolId)
    {
        _expandToSymbolTargetId = symbolId;
        _expandToSymbolTriggered = true;
        _scrollToSymbolId = symbolId;

        // The highlight normally mirrors the graph selection. Hold the revealed symbol
        // highlighted until that selection changes, so reveals from documentation links
        // (which don't select anything) stay visible.
        _revealHighlightActive = true;
        _graphSelectionAtReveal = TryGetSingleSelectedSymbolId();
        Config.Visible = true;
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
    //private static bool _refreshTriggered;

    private static readonly TreeHandler _treeHandler = new();

    /// <summary>
    /// Draws the main symbol library view, including search bar, filters, and the result tree.
    /// </summary>
    private void DrawView()
    {
        _treeHandler.Update();

        var iconCount = 2;
        if (_wasScanned)
            iconCount++;

        // Draw search input field
        CustomComponents.DrawInputFieldWithPlaceholder(
                                                       "Search symbols...",
                                                       ref _filter.SearchString,
                                                       -ImGui.GetFrameHeight() * iconCount + 16);

        // Collapse icon
        {
            ImGui.SameLine();
            var collapseIconState = _treeHandler.NoFolderOpen
                                        ? CustomComponents.ButtonStates.Default
                                        : CustomComponents.ButtonStates.Emphasized;

            if (CustomComponents.IconButton(Icon.TreeCollapse, Vector2.Zero, collapseIconState))
            {
                _treeHandler.CollapseAll();
            }
        }

        ImGui.SameLine();
        // Draw refresh button and handle refresh logic
        if (CustomComponents.IconButton(Icon.Refresh, Vector2.Zero, CustomComponents.ButtonStates.Default) || NeedsRebuild)
        {
            UpdateSymbolLibraryState();
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
        ImGui.BeginChild("scrolling", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.NoBackground);
        {
            // Show filtered or full tree depending on filter/search state
            if (_libraryFiltering.AnyFilterActive)
            {
                UpdatePathToFocusedSymbol(FilteredTree);
                DrawNode(FilteredTree);
            }
            else if (string.IsNullOrEmpty(_filter.SearchString))
            {
                UpdatePathToFocusedSymbol(_treeNode);
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

        CustomComponents.HandleDragScrolling(this);
        ImGui.EndChild();
    }

    /// <summary>
    /// Updates the symbol library state by repopulating the tree and updating analysis details.
    /// </summary>
    private void UpdateSymbolLibraryState()
    {
        _treeHandler.Reset();
        _treeNode.PopulateCompleteTree();
        ExampleSymbolLinking.UpdateExampleLinks();
        SymbolAnalysis.UpdateDetails();
        _wasScanned = true;
        _lastUpdateVersion = EditorSymbolPackage.SymbolStructureVersionCounter;
        
    }

    private bool NeedsRebuild => _lastUpdateVersion < EditorSymbolPackage.SymbolStructureVersionCounter;

    private int _lastUpdateVersion;

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

            CustomComponents.HandleDragScrolling(this);
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

    // Set by Reveal() and the aim icon: the symbol item scrolls itself into view once, then clears this.
    private Guid? _scrollToSymbolId;

    // Nodes leading to the highlighted symbol. Rebuilt once per frame and shared by the aim icon and
    // the expand-on-click: a symbol id can be held by more than one node (two packages registering the
    // same id), and two independent lookups would disagree on which one to point at.
    private static readonly List<NamespaceTreeNode> _pathToFocusedSymbol = [];

    /// <summary>
    /// Collects the nodes leading to the symbol the tree should highlight or expand to. Prefers the node
    /// matching the symbol's registered namespace, so a stray duplicate elsewhere can't win.
    /// </summary>
    private void UpdatePathToFocusedSymbol(NamespaceTreeNode root)
    {
        _pathToFocusedSymbol.Clear();

        var focusedId = _expandToSymbolTriggered && _expandToSymbolTargetId.HasValue
                            ? _expandToSymbolTargetId
                            : _lastSelectedSymbolId;

        if (focusedId.HasValue && !TryFindPathByNamespace(root, focusedId.Value, _pathToFocusedSymbol))
        {
            if (FindPathRecursive(root, focusedId.Value, _pathToFocusedSymbol))
            {
                // FindPathRecursive collects on the way out, so it yields the holder first.
                _pathToFocusedSymbol.Reverse();
            }
            else
            {
                _pathToFocusedSymbol.Clear();
            }
        }

        // A target that isn't reachable, or sits in the root node that draws no tree node, would never
        // clear the trigger while drawing - and a stuck trigger forces every path node open each frame.
        if (_pathToFocusedSymbol.Count <= 1)
        {
            _expandToSymbolTriggered = false;
            _expandToSymbolTargetId = null;
        }
    }

    /// <summary>
    /// Walks straight down the symbol's registered namespace and verifies the reached node holds it.
    /// </summary>
    private static bool TryFindPathByNamespace(NamespaceTreeNode root, Guid symbolId, List<NamespaceTreeNode> path)
    {
        if (!SymbolUiRegistry.TryGetSymbolUi(symbolId, out var symbolUi))
            return false;

        var nameSpace = symbolUi.Symbol.Namespace;
        if (string.IsNullOrEmpty(nameSpace))
            return false;

        var node = root;
        path.Add(node);

        foreach (var part in nameSpace.Split('.'))
        {
            if (part.Length == 0)
                continue;

            NamespaceTreeNode? match = null;
            foreach (var child in node.Children)
            {
                if (child.Name != part)
                    continue;

                match = child;
                break;
            }

            if (match == null)
            {
                path.Clear();
                return false;
            }

            node = match;
            path.Add(node);
        }

        foreach (var symbol in node.Symbols)
        {
            if (symbol.Id == symbolId)
                return true;
        }

        path.Clear();
        return false;
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

    private static string _draggedNamespace = string.Empty;

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
            ImGui.PushID(subtree.Id);
            ImGui.SetNextItemWidth(10);
            if (subtree.Name == "Lib" && !_openedLibFolderOnce)
            {
                ImGui.SetNextItemOpen(true);
                _openedLibFolderOnce = true;
            }

            // --- Aim icon logic for tree nodes ---
            var selectedSymbolId = _lastSelectedSymbolId;
            var containsSelected = _pathToFocusedSymbol.Contains(subtree);

            // Expand all nodes in the path to the target symbol if triggered
            if (_expandToSymbolTriggered && containsSelected)
            {
                ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            }

            _treeHandler.UpdateForNode(subtree.Id);
            var isProject = subtree.FolderType == NamespaceTreeNode.SymbolFolderTypes.Project;
            if (isProject)
            {
                ImGui.PushFont(Fonts.FontBold);
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Fade(0.7f).Rgba);
            }

            // Tight rows and subtle hover matching the folder styling of the asset library
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Color.Transparent.Rgba);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, Color.Transparent.Rgba);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            var isOpen = ImGui.TreeNode(subtree.Name);
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor(2);

            CustomComponents.DrawHoverHighlightOnLastItem();

            if (isProject)
            {
                ImGui.PopFont();
            }
            else
            {
                ImGui.PopStyleColor();
            }

            if (DragAndDropHandling.HandleDragSourceForLastItem(
                                                                DragAndDropHandling.DragTypes.SymbolNameSpace,
                                                                subtree.Namespace))
            {
                _draggedNamespace = subtree.Namespace;
            }

            // Draw aim icon if this node contains the selected symbol and is not open
            if (!isOpen && containsSelected)
            {
                var h = ImGui.GetFontSize();
                CustomComponents.RightAlign(h);

                var clicked = ImGui.InvisibleButton("Reveal", new Vector2(h));
                if (ImGui.IsItemHovered())
                {
                    var isComposition = ProjectView.Focused?.CompositionInstance?.Symbol.Id == selectedSymbolId;
                    CustomComponents.TooltipForLastItem(isComposition
                                                            ? "Reveal current composition"
                                                            : "Reveal selected operator");
                }

                // Animate aim icon
                var timeSinceChange = (float)(ImGui.GetTime() - _lastSelectionTime);
                var fadeProgress = Clamp01(timeSinceChange / 0.5f);
                var blinkFade = -MathF.Cos(timeSinceChange * 15f) * (1f - fadeProgress) * 0.7f + 0.75f;
                var color = UiColors.StatusActivated.Fade(blinkFade);
                Icons.DrawIconOnLastItem(Icon.Aim, color);

                // Set expand trigger if clicked. The symbol row scrolls itself into view via
                // _scrollToSymbolId once the path is expanded.
                if (clicked && selectedSymbolId.HasValue)
                {
                    _expandToSymbolTriggered = true;
                    _expandToSymbolTargetId = selectedSymbolId;
                    _scrollToSymbolId = selectedSymbolId;
                }
            }

            // Context menu for namespace node
            CustomComponents.ContextMenuForItem(() =>
                                                {
                                                    if (CustomComponents.DrawMenuItem(_renameNamespaceId, "Rename Namespace...", reserveIconColumn: false))
                                                    {
                                                        _subtreeNodeToRename = subtree;
                                                        _renameNamespaceDialog.ShowNextFrame();
                                                    }

                                                    // Nodes above a project root (e.g. the "user" row) group several projects
                                                    // and don't map to a folder of their own.
                                                    var mapsToFolder = subtree.FolderType is NamespaceTreeNode.SymbolFolderTypes.Project
                                                                           or NamespaceTreeNode.SymbolFolderTypes.ProjectSubNamespace;
                                                    var symbolInNamespace = mapsToFolder ? TryFindFirstSymbolRecursive(subtree) : null;

                                                    if (CustomComponents.DrawMenuItem(_revealNamespaceInExplorerId,
                                                                                      isProject ? "Reveal Project in Explorer" : "Reveal Namespace in Explorer",
                                                                                      isEnabled: symbolInNamespace != null,
                                                                                      reserveIconColumn: false))
                                                    {
                                                        var package = symbolInNamespace!.SymbolPackage;
                                                        RevealInExplorer(SymbolPathHandler.GetDirectoryForNamespace(subtree.Namespace,
                                                                                   package.RootNamespace,
                                                                                   package.Folder));
                                                    }
                                                });

            HandleDroppingNamespace(subtree);

            if (isOpen)
            {
                _treeHandler.NoFolderOpen = false;

                // The deepest node of the path is open, so the target row is about to be drawn.
                if (_expandToSymbolTriggered && _pathToFocusedSymbol.Count > 0 && _pathToFocusedSymbol[^1] == subtree)
                {
                    _expandToSymbolTriggered = false;
                    _expandToSymbolTargetId = null;
                }

                HandleDroppingSymbol(subtree);

                DrawNodeItems(subtree);

                ImGui.TreePop();
            }
            // else
            // {
            //     // Small helper button for quickly dropping dragged symbols into unopened namespaces.
            //     if (DragAndDropHandling.IsDragging)
            //     {
            //         ImGui.SameLine();
            //         ImGui.PushID("DropButton");
            //         ImGui.Button("  <-", new Vector2(50, 15));
            //         HandleDropTarget(subtree);
            //         ImGui.PopID();
            //     }
            // }

            ImGui.PopID();
        }
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
    private static void HandleDroppingSymbol(NamespaceTreeNode subtree)
    {
        var resultSymbol = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.Symbol, out var data);

        if (resultSymbol != DragAndDropHandling.DragInteractionResult.Dropped)
            return;

        if (!Guid.TryParse(data, out var symbolId))
            return;

        if (!MoveSymbolToNamespace(symbolId, subtree.GetAsString(), out var reason))
            BlockingWindow.Instance.ShowMessageBox(reason, "Could not move symbol's namespace");

        EditorSymbolPackage.NotifySymbolStructureChange();
    }

    /// <summary>
    /// Handles drag-and-drop onto a namespace node to move symbols between namespaces.
    /// </summary>
    private static void HandleDroppingNamespace(NamespaceTreeNode subtree)
    {
        if (subtree.Namespace.StartsWith(_draggedNamespace))
            return;

        var targetNamespace = subtree.Namespace;

        var resultSymbol = DragAndDropHandling.TryHandleDropOnItem(DragAndDropHandling.DragTypes.SymbolNameSpace, out var data);

        if (resultSymbol != DragAndDropHandling.DragInteractionResult.Dropped)
            return;

        if (!EditableSymbolProject.TryGetEditableProjectOfNamespace(_draggedNamespace, out var sourceProject))
        {
            Log.Warning($"Can't find editable project for {_draggedNamespace}");
            return;
        }

        if (!EditableSymbolProject.TryGetEditableProjectOfNamespace(targetNamespace, out var targetProject))
        {
            Log.Warning($"Can't find editable project for {_draggedNamespace}");
            return;
        }

        List<Symbol> draggedSymbols = [];

        foreach (var symbol in sourceProject.Symbols.Values)
        {
            if (!symbol.Namespace.StartsWith(_draggedNamespace, StringComparison.OrdinalIgnoreCase))
                continue;

            draggedSymbols.Add(symbol);
        }

        var lastNamespacePartIndex = _draggedNamespace.LastIndexOf('.');
        var lastNamespacePart = lastNamespacePartIndex > 0
                                    ? _draggedNamespace[(lastNamespacePartIndex + 1)..]
                                    : _draggedNamespace;

        for (var index = 0; index < draggedSymbols.Count; index++)
        {
            var symbol = draggedSymbols[index];
            var target = targetNamespace;

            if (symbol.Namespace.StartsWith(_draggedNamespace, StringComparison.OrdinalIgnoreCase)
                && symbol.Namespace.Length > _draggedNamespace.Length)
            {
                target += (symbol.Namespace[_draggedNamespace.Length..]);
            }
            else
            {
                target = targetNamespace + "." + lastNamespacePart;
            }

            var skipProjectReload = index < draggedSymbols.Count - 1;
            if (!MoveSymbolToNamespace(symbol.Id, target, out var reason, skipProjectReload))
            {
                Log.Warning($"Failed to move {symbol} to {target}: {reason}");
            }
        }

        EditorSymbolPackage.NotifySymbolStructureChange();
    }

    /// <summary>
    /// Moves a symbol to a new namespace, respecting read-only package restrictions.
    /// </summary>
    private static bool MoveSymbolToNamespace(Guid symbolId, string nameSpace, out string reason, bool skipProjectReload = false)
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

        return EditableSymbolProject.ChangeSymbolNamespace(symbolUi.Symbol, nameSpace, out reason, skipProjectReload);
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

    // Corner rounding (unscaled px) for symbol item buttons and their badges
    private const float ItemRounding = 3f;

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

    // A highlight from Reveal() persists until the graph selection changes away from this snapshot.
    private static bool _revealHighlightActive;
    private static Guid? _graphSelectionAtReveal;

    /// <summary>
    /// Updates the highlight/aim icon state for the currently selected symbol in the node graph.
    /// </summary>
    private void UpdateSelectedSymbolHighlight()
    {
        if (ProjectView.Focused?.NodeSelection == null)
            return;

        var selectedSymbolId = TryGetSingleSelectedSymbolId();

        // A Reveal() highlight is not backed by the graph selection - keep it
        // (set via _scrollToSymbolId in DrawSymbolItemInstance) until that selection changes.
        if (_revealHighlightActive)
        {
            if (selectedSymbolId == _graphSelectionAtReveal)
                return;

            _revealHighlightActive = false;
        }

        // Only highlight if exactly one operator is selected in the node graph
        if (selectedSymbolId.HasValue)
        {
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

    /// <summary>
    /// The symbol the library highlights: the single selected operator, or - with nothing selected -
    /// the current composition, so the graph's own symbol can be located too.
    /// </summary>
    private static Guid? TryGetSingleSelectedSymbolId()
    {
        var projectView = ProjectView.Focused;
        var nodeSelection = projectView?.NodeSelection;
        if (nodeSelection == null)
            return null;

        // Iterate instead of GetSelectedChildUis().ToList() - this runs every frame.
        SymbolUi.Child? singleSelectedChildUi = null;
        var selectedChildCount = 0;
        foreach (var node in nodeSelection.Selection)
        {
            if (node is not SymbolUi.Child childUi)
                continue;

            selectedChildCount++;
            if (selectedChildCount > 1)
                return null;

            singleSelectedChildUi = childUi;
        }

        if (singleSelectedChildUi != null)
            return singleSelectedChildUi.SymbolChild?.Symbol.Id;

        return projectView?.CompositionInstance?.Symbol.Id;
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

            // A pending Reveal() scrolls this item into view and highlights it. The path nodes are
            // expanded by the _expandToSymbolTargetId logic above, so by here the item is laid out.
            if (_scrollToSymbolId == symbol.Id)
            {
                ImGui.SetScrollHereY(0.5f);
                _lastSelectedSymbolId = symbol.Id;
                _lastSelectionTime = ImGui.GetTime();
                _scrollToSymbolId = null;
            }

            // Skip layout and badge work for rows outside the visible scroll region — the tree
            // can hold thousands of symbols. A dummy of the same height keeps scroll extents stable.
            var rowHeight = ImGui.GetFrameHeight();
            if (!ImGui.IsRectVisible(new Vector2(1, rowHeight)))
            {
                ImGui.Dummy(new Vector2(1, rowHeight));
                ImGui.PopID();
                return;
            }

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

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ItemRounding * T3Ui.UiScaleFactor);
            ImGui.PushStyleColor(ImGuiCol.Button, ColorVariations.OperatorBackground.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.Text, ColorVariations.OperatorLabel.Apply(color).Rgba);

            bool buttonPressed = ImGui.Button(symbol.Name.AddSpacesForImGuiOutput());
            ImGui.PopStyleVar();
            if (buttonPressed)
                HelpWindow.ShowTopic(HelpTopic.ForOperator(symbol.Id));

            // Get button rect for icon and highlight
            var buttonMin = ImGui.GetItemRectMin();
            var buttonMax = ImGui.GetItemRectMax();

            // Draw highlight border if selected (drawn last, on top)
            if (isSelected)
            {
                var fadeProgress = Clamp01(timeSinceSelection / 0.3f);
                var blinkFade = MathUtils.Lerp(-MathF.Cos(timeSinceSelection * 15f) * 0.8f + 0.2f, 1, fadeProgress);

                var highlightColor = UiColors.StatusActivated.Fade(blinkFade);
                ImGui.GetWindowDrawList().AddRect(buttonMin, buttonMax, highlightColor, ItemRounding * T3Ui.UiScaleFactor);
            }

            // Show tooltip with description if hovered
            if (ImGui.IsItemHovered())
            {
                // Let the Help window follow the symbol the mouse is over here, just like hovering it in the graph.
                HoveredHelpTarget.SetOperator(symbol.Id);

                var thumbnailRect = ThumbnailManager.GetThumbnail(symbol.Id, symbol.SymbolPackage, ThumbnailManager.Categories.PackageMeta);
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);

                // The Help window already previews the hovered symbol — the tooltip would double it.
                if (!HelpWindow.HoverPreviewActive
                    && (!string.IsNullOrEmpty(symbolUi.Description) || thumbnailRect.IsReady))
                {
                    CustomComponents.BeginTooltip(600);
                    {
                        if (!string.IsNullOrEmpty(symbolUi.Description))
                        {
                            ImGui.BeginGroup();
                            {
                                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 25.0f);
                                ImGui.TextUnformatted(symbolUi.Description);
                                ImGui.PopTextWrapPos();
                            }
                            ImGui.EndGroup();
                        }

                        if (thumbnailRect.IsReady)
                        {
                            ImGui.SameLine(0, 10);

                            ImGui.BeginGroup();
                            {
                                thumbnailRect.AsImguiImage();
                            }
                            ImGui.EndGroup();
                        }
                    }
                    CustomComponents.EndTooltip();
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

                                                                   if (CustomComponents.DrawMenuItem(_revealSymbolInExplorerId, "Reveal Symbol in Explorer",
                                                                                                     reserveIconColumn: false))
                                                                   {
                                                                       RevealInExplorer(GetFileOfSymbol(symbol));
                                                                   }

                                                                   if (CustomComponents.DrawMenuItem(_revealProjectInExplorerId, "Reveal Project in Explorer",
                                                                                                     reserveIconColumn: false))
                                                                   {
                                                                       RevealInExplorer(symbol.SymbolPackage.Folder);
                                                                   }

                                                                   CustomComponents.SeparatorLine();

                                                                   // Delete symbol menu entry
                                                                   if (CustomComponents.DrawMenuItem(_deleteSymbolId, "Delete Symbol", reserveIconColumn: false))
                                                                   {
                                                                       _symbolToDelete = symbol;
                                                                       _deleteSymbolDialog.ShowNextFrame();
                                                                   }
                                                               },
                                                title: symbol.Name,
                                                id: "##symbolTreeSymbolContextMenu");

            // Experimental Thumbnails
            // var keepCursor = ImGui.GetCursorPos();
            // CustomComponents.RightAlign(50);
            // if (!ThumbnailManager.GetThumbnail(symbol.Id, symbol.SymbolPackage).AsImguiImage(30))
            // {
            //     ImGui.Dummy(new Vector2(10));
            // }
            // ImGui.SetCursorPos(keepCursor);

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
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, ItemRounding * T3Ui.UiScaleFactor);
                ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f * ImGui.GetStyle().Alpha);
                for (var index = 0; index < examples.Count; index++)
                {
                    var exampleSymbolUi = examples[index];
                    ImGui.SameLine();
                    ImGui.Button("EXAMPLE");
                    HandleDragAndDropForSymbolItem(exampleSymbolUi.Symbol);
                }

                ImGui.PopStyleVar(2);
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
    internal static bool IsSymbolCurrentCompositionOrAParent(Symbol symbol)
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

    /// <summary>
    /// The file to select when revealing a symbol: its C# source, or the <c>.t3</c> operator file
    /// for packages that ship without sources.
    /// </summary>
    private static string GetFileOfSymbol(Symbol symbol)
    {
        var package = symbol.SymbolPackage;
        if (package is EditorSymbolPackage editorPackage
            && editorPackage.TryGetSourceCodePath(symbol, out var sourceCodePath)
            && !string.IsNullOrEmpty(sourceCodePath))
        {
            return sourceCodePath;
        }

        var folder = SymbolPathHandler.GetDirectoryForNamespace(symbol.Namespace, package.RootNamespace, package.Folder);
        return Path.Combine(folder, symbol.Name + SymbolPackage.SymbolExtension);
    }

    private static Symbol? TryFindFirstSymbolRecursive(NamespaceTreeNode node)
    {
        if (node.Symbols.Count > 0)
            return node.Symbols[0];

        foreach (var child in node.Children)
        {
            var symbol = TryFindFirstSymbolRecursive(child);
            if (symbol != null)
                return symbol;
        }

        return null;
    }

    /// <summary>
    /// Reveals a file or folder, falling back to the containing folder when the file itself is gone
    /// (an operator can be in memory before its files are written).
    /// </summary>
    private static void RevealInExplorer(string path)
    {
        if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
        {
            CoreUi.Instance.RevealInFileBrowser(path);
            return;
        }

        var folder = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            Log.Warning($"Can't reveal missing path '{path}'");
            return;
        }

        CoreUi.Instance.RevealInFileBrowser(folder);
    }


    private static readonly int _renameNamespaceId = nameof(_renameNamespaceId).GetHashCode();
    private static readonly int _deleteSymbolId = nameof(_deleteSymbolId).GetHashCode();
    private static readonly int _revealSymbolInExplorerId = nameof(_revealSymbolInExplorerId).GetHashCode();
    private static readonly int _revealProjectInExplorerId = nameof(_revealProjectInExplorerId).GetHashCode();
    private static readonly int _revealNamespaceInExplorerId = nameof(_revealNamespaceInExplorerId).GetHashCode();
}