#nullable enable

using ImGuiNET;
using T3.Core.Operator;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Graph.Dialogs;
using T3.Editor.Gui.Graph.Legacy.Interaction.Connections;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows;

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
        if(_subtreeNodeToRename != null)
            _renameNamespaceDialog.Draw(_subtreeNodeToRename);

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


    private void DrawView()
    {
        var iconCount = 1;
        if (_wasScanned)
            iconCount++;

        CustomComponents.DrawInputFieldWithPlaceholder("Search symbols...", ref _filter.SearchString, -ImGui.GetFrameHeight() * iconCount + 16);

        ImGui.SameLine();
        if (CustomComponents.IconButton(Icon.Refresh, Vector2.Zero, CustomComponents.ButtonStates.Dimmed))
        {
            _treeNode.PopulateCompleteTree();
            ExampleSymbolLinking.UpdateExampleLinks();
            SymbolAnalysis.UpdateDetails();
            _wasScanned = true;
        }

        CustomComponents.TooltipForLastItem("Scan usage dependencies for symbols", 
                                            "This can be useful for cleaning up operator name spaces.");

        if (_wasScanned)
        {
            _libraryFiltering.DrawSymbolFilters();
        }

        ImGui.BeginChild("scrolling");
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
                if (SymbolAnalysis.DetailsInitialized && SymbolAnalysis.InformationForSymbolIds.TryGetValue(_symbolUsageReferenceFilter.Id, out var info))
                {
                    // TODO: this should be cached...
                    var allSymbols = EditorSymbolPackage.AllSymbols.ToDictionary(s => s.Id);

                    foreach (var id in info.DependingSymbols)
                    {
                        if (allSymbols.TryGetValue(id, out var symbolUi))
                        {
                            DrawSymbolItem(symbolUi);
                        }
                    }
                }
            }
            ImGui.EndChild();
        }
    }


    private void DrawFilteredList()
    {
        _filter.UpdateIfNecessary(null);
        foreach (var symbolUi in _filter.MatchingSymbolUis)
        {
            DrawSymbolItem(symbolUi.Symbol);
        }
    }

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

    private void DrawNodeItems(NamespaceTreeNode subtree)
    {
        // Using a for loop to prevent modification during iteration exception
        for (var index = 0; index < subtree.Children.Count; index++)
        {
            var subspace = subtree.Children[index];
            DrawNode(subspace);
        }

        for (var index = 0; index < subtree.Symbols.ToList().Count; index++)
        {
            var symbol = subtree.Symbols.ToList()[index];
            DrawSymbolItem(symbol);
        }
    }

    private static void HandleDropTarget(NamespaceTreeNode subtree)
    {
        if (!DragAndDropHandling.TryGetDataDroppedLastItem(DragAndDropHandling.SymbolDraggingId, out var data))
            return;

        if (!Guid.TryParse(data, out var symbolId))
            return;

        if (!MoveSymbolToNamespace(symbolId, subtree.GetAsString(), out var reason))
            BlockingWindow.Instance.ShowMessageBox(reason, "Could not move symbol's namespace");
    }

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

    internal static void DrawSymbolItem(Symbol symbol)
    {
        ImGui.PushID(symbol.Id.GetHashCode());
        {
            var color = symbol.OutputDefinitions.Count > 0
                            ? TypeUiRegistry.GetPropertiesForType(symbol.OutputDefinitions[0]?.ValueType).Color
                            : UiColors.Gray;

            var symbolUi = symbol.GetSymbolUi();

            // var state = ParameterWindow.GetButtonStatesForSymbolTags(symbolUi.Tags);
            // if (CustomComponents.IconButton(Icon.Bookmark, Vector2.Zero, state))
            // {
            //     
            // }
            if(ParameterWindow.DrawSymbolTagsButton(symbolUi)) 
                symbolUi.FlagAsModified();
            
            ImGui.SameLine();
            
            ImGui.PushStyleColor(ImGuiCol.Button, ColorVariations.OperatorBackground.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ColorVariations.OperatorBackgroundHover.Apply(color).Rgba);
            ImGui.PushStyleColor(ImGuiCol.Text, ColorVariations.OperatorLabel.Apply(color).Rgba);

            if (ImGui.Button(symbol.Name))
            {
                //_selectedSymbol = symbol;
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                
                if (!string.IsNullOrEmpty(symbolUi.Description))
                {
                    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(4,4));
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

            CustomComponents.ContextMenuForItem(drawMenuItems: () => CustomComponents.DrawSymbolCodeContextMenuItem(symbol), 
                                                title: symbol.Name,
                                                id: "##symbolTreeSymbolContextMenu");

            if (SymbolAnalysis.DetailsInitialized && SymbolAnalysis.InformationForSymbolIds.TryGetValue(symbol.Id, out var info))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                
                
                ListSymbolSetWithTooltip(250,Icon.Dependencies,"{0}", string.Empty, "requires...", info.RequiredSymbolIds.ToList());
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ListSymbolSetWithTooltip(300,Icon.None,"{0}", string.Empty, "has invalid references...", info.InvalidRequiredIds);
                ImGui.PopStyleColor();
                
                if (ListSymbolSetWithTooltip(340, Icon.Referenced, "{0}", " NOT USED",  "used by...", info.DependingSymbols.ToList()))
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
                    ImGui.Button($"EXAMPLE");
                    HandleDragAndDropForSymbolItem(exampleSymbolUi.Symbol);
                }

                ImGui.PopStyleVar();
                ImGui.PopFont();
            }
                
        }
        ImGui.PopID();
    }

    private static bool ListSymbolSetWithTooltip(float x, Icon icon, string setTitleFormat, string emptySetTitle, string toolTopTitle, List<Guid> symbolSet)
    {
        var activated = false;
        ImGui.PushID(icon.ToString());
        ImGui.SameLine(x,10);
        if (symbolSet.Count > 0)
        {
            icon.Draw();
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
        //ImGui.SameLine();
        return activated;

        void DrawTooltip()
        {
            var allSymbolUis = EditorSymbolPackage
               .AllSymbolUis;

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
            
            var hasIssues = required.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete) | required.Tags.HasFlag(SymbolUi.SymbolTags.NeedsFix);
            var color = hasIssues ? UiColors.StatusAttention : UiColors.Text;
            ImGui.PushStyleColor(ImGuiCol.Text, color.Rgba);
            ColumnLayout.StartGroupAndWrapIfRequired(1);
            ImGui.TextUnformatted(required.Symbol.Name);
            ColumnLayout.ExtendWidth(ImGui.GetItemRectSize().X);
            ImGui.PopStyleColor();
        }
    }

    internal static void HandleDragAndDropForSymbolItem(Symbol symbol)
    {
        if (IsSymbolCurrentCompositionOrAParent(symbol))
            return;

        DragAndDropHandling.HandleDragSourceForLastItem(DragAndDropHandling.SymbolDraggingId, symbol.Id.ToString(), "Create instance");

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

/// <summary>
/// A simple ui to filter operators for certain properties like missing descriptions, invalid references, etc.
/// </summary>
/// <remarks>
/// It is only shown after scanning the library by pressing the update icon.
/// </remarks>
internal sealed class LibraryFiltering(SymbolLibrary symbolLibrary)
{
    internal void DrawSymbolFilters()
    {
        ImGui.SameLine();
        var status = _showFilters ? CustomComponents.ButtonStates.Activated : CustomComponents.ButtonStates.Dimmed;

        if (CustomComponents.IconButton(Icon.Flame, Vector2.Zero, status))
            _showFilters = !_showFilters;

        CustomComponents.TooltipForLastItem("Show problem filters", "Allows filter operators to different problems and attributes.");

        if (!_showFilters)
            return;

        ImGui.Indent();
        
        var opInfos = SymbolAnalysis.InformationForSymbolIds.Values;
        
        var totalOpCount = _onlyInLib
                               ? opInfos.Count(i => i.IsLibOperator)
                               : opInfos.Count;
        
        CustomComponents.SmallGroupHeader($"Out of {totalOpCount} show those with...");
        
        var needsUpdate = false;
        needsUpdate |= DrawFilterToggle("Help missing ({0})",
                                        opInfos.Count(i => i.LacksDescription && (i.IsLibOperator || !_onlyInLib)),
                                        Flags.MissingDescriptions,
                                        ref _activeFilters);
        
        needsUpdate |= DrawFilterToggle("Parameter help missing ({0})",
                                        opInfos.Count(i => i.LacksAllParameterDescription && (i.IsLibOperator || !_onlyInLib)),
                                        Flags.MissingAllParameterDescriptions,
                                        ref _activeFilters);
        
        needsUpdate |= DrawFilterToggle("Parameter help incomplete",
                                        0,
                                        Flags.MissingSomeParameterDescriptions,
                                        ref _activeFilters);
        
        needsUpdate |= DrawFilterToggle("No grouping ({0})",
                                        opInfos.Count(i => i.LacksParameterGrouping && (i.IsLibOperator || !_onlyInLib)),
                                        Flags.MissingParameterGrouping,
                                        ref _activeFilters);
        
        needsUpdate |= DrawFilterToggle("Unused ({0})",
                                        opInfos.Count(i => i.DependingSymbols.Count == 0 && (i.IsLibOperator || !_onlyInLib)),
                                        Flags.Unused,
                                        ref _activeFilters);
        
        needsUpdate |= DrawFilterToggle("Invalid Op dependencies ({0})",
                                        opInfos.Count(i => i.InvalidRequiredIds.Count > 0 && (i.IsLibOperator || !_onlyInLib)),
                                        Flags.InvalidRequiredOps,
                                        ref _activeFilters);

        needsUpdate |= DrawFilterToggle("Depends on obsolete ops ({0})",
                                        opInfos.Count(i => i.DependsOnObsoleteOps && (i.IsLibOperator || !_onlyInLib)),
                                        Flags.DependsOnObsoleteOps,
                                        ref _activeFilters);
        
        FormInputs.AddVerticalSpace(5);

        needsUpdate |= DrawFilterToggle("Obsolete ({0})",
                                        opInfos.Count(i => i.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete) && (i.IsLibOperator || !_onlyInLib)),
                                        Flags.Obsolete,
                                        ref _activeFilters);

        needsUpdate |= DrawFilterToggle("NeedsFix ({0})",
                                        opInfos.Count(i => i.Tags.HasFlag(SymbolUi.SymbolTags.NeedsFix) && (i.IsLibOperator || !_onlyInLib)),
                                        Flags.NeedsFix,
                                        ref _activeFilters);

        
        
        
        FormInputs.AddVerticalSpace(5);
        needsUpdate |= ImGui.Checkbox("Only in Lib", ref _onlyInLib);
        
        ImGui.Unindent();


        if (needsUpdate)
        {
            symbolLibrary.FilteredTree.PopulateCompleteTree(s =>
                                                            {
                                                                var info = SymbolAnalysis.InformationForSymbolIds[s.Symbol.Id];
                                                                if (_onlyInLib && !info.IsLibOperator)
                                                                    return false;

                                                                if (!AnyFilterActive)
                                                                    return true;

                                                                return _activeFilters.HasFlag(Flags.MissingDescriptions) && info.LacksDescription
                                                                       || _activeFilters.HasFlag(Flags.MissingAllParameterDescriptions) && info.LacksAllParameterDescription
                                                                       || _activeFilters.HasFlag(Flags.MissingSomeParameterDescriptions) && info.LacksSomeParameterDescription
                                                                       || _activeFilters.HasFlag(Flags.MissingParameterGrouping) && info.LacksParameterGrouping
                                                                       || _activeFilters.HasFlag(Flags.InvalidRequiredOps) && info.InvalidRequiredIds.Count > 0
                                                                       || _activeFilters.HasFlag(Flags.Unused) && info.DependingSymbols.Count == 0
                                                                       || _activeFilters.HasFlag(Flags.Obsolete) && info.Tags.HasFlag(SymbolUi.SymbolTags.Obsolete)
                                                                       || _activeFilters.HasFlag(Flags.NeedsFix) && info.Tags.HasFlag(SymbolUi.SymbolTags.NeedsFix)
                                                                       || _activeFilters.HasFlag(Flags.DependsOnObsoleteOps) && info.DependsOnObsoleteOps
                                                                       ;
                                                            });
        }

        ImGui.Separator();
        FormInputs.AddVerticalSpace();
        CustomComponents.SmallGroupHeader($"Result...");
    }

    private static bool DrawFilterToggle(string label, int count, Flags filterFlag, ref Flags activeFlags)
    {
        var isActive = activeFlags.HasFlag(filterFlag);
        var clicked = ImGui.Checkbox(string.Format(label, count), ref isActive);
        if (clicked)
        {
            activeFlags ^= filterFlag;
        }

        return clicked;
    }

    [Flags]
    private enum Flags
    {
        None = 0, // auto increment shift
        MissingDescriptions = 1<<1,
        MissingAllParameterDescriptions = 1 << 2,
        MissingSomeParameterDescriptions = 1 << 3,
        MissingParameterGrouping = 1 << 4,
        InvalidRequiredOps = 1 << 5,
        Unused = 1 << 6,
        Obsolete = 1<<7,
        NeedsFix = 1<<8,
        DependsOnObsoleteOps = 1<<9,

    }

    private bool _onlyInLib = true;
    private Flags _activeFilters;
    
    private bool _showFilters;
    internal bool AnyFilterActive => _activeFilters != Flags.None;
}

/// <summary>
/// A fun little experiment that shows random prompt suggestions, when entering '?' into the search field.
/// The number of characters defines the count. The relevancy threshold limits operator to a meaningful set. 
/// </summary>
internal sealed class RandomPromptGenerator(SymbolFilter filter)
{
    private int _randomSeed;
    private List<Symbol>? _allLibSymbols;
    private float _promptComplexity = 0.25f;

    internal void DrawRandomPromptList()
    {
        ImGui.Indent();
        FormInputs.AddSectionHeader("Random Prompts");

        var listNeedsUpdate = _allLibSymbols == null;
        FormInputs.SetIndent(80 * T3Ui.UiScaleFactor);
        FormInputs.AddInt("Seed", ref _randomSeed);
        listNeedsUpdate |= FormInputs.AddFloat("Complexity", ref _promptComplexity, 0, 1, 0.02f, true, true);
        FormInputs.SetIndentToLeft();

        FormInputs.AddVerticalSpace();

        // Rebuild list if necessary
        if (listNeedsUpdate)
        {
            // Count all lib ops
            if (_allLibSymbols == null)
            {
                _allLibSymbols = new List<Symbol>();
                foreach (var s in EditorSymbolPackage.AllSymbols)
                {
                    if (s.Namespace.StartsWith("Lib.") && !s.Name.StartsWith("_"))
                        _allLibSymbols.Add(s);
                }
            }

            // Filter 
            var limit = (int)(_allLibSymbols.Count * _promptComplexity).Clamp(1, _allLibSymbols.Count - 1);
            var keep = filter.SearchString;
            filter.SearchString = "Lib.";
            filter.UpdateIfNecessary(null, true, limit);
            filter.SearchString = keep;
        }

        var relevantCount = filter.MatchingSymbolUis.Count;

        if (_randomSeed == 0)
        {
            _randomSeed = (int)(ImGui.GetFrameCount() * 374761393U & 1023U);
        }

        var promptCount = filter.SearchString.Count(c => c == '?');
        for (uint i = 0; i < promptCount; i++)
        {
            var f = MathUtils.Hash01((uint)((i + 42 * _randomSeed * 668265263U) & 0x7fffffff));
            var randomIndex = (int)(f * relevantCount).Clamp(0, relevantCount - 1);
            SymbolLibrary.DrawSymbolItem(filter.MatchingSymbolUis[randomIndex].Symbol);
        }
    }
}