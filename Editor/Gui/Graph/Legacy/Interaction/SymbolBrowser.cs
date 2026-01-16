using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.Legacy.Interaction.Connections;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Commands;
using T3.Editor.UiModel.Commands.Graph;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.SystemUi;

namespace T3.Editor.Gui.Legacy.Interaction;

/// <summary>
/// Represents a placeholder for a new <see cref="GraphNode"/> on the <see cref="GraphView"/>. 
/// It can be connected to other nodes and provide search functionality. It's basically the
/// T2's CreateOperatorWindow.
/// </summary>
internal sealed class SymbolBrowser
{
    private readonly ProjectView _components;
    private readonly IGraphView _graphView;
        
    public SymbolBrowser(ProjectView components, IGraphView graphView)
    {
        _components = components;
        _graphView = graphView;
    }
    #region public API ------------------------------------------------------------------------

    public void OpenAt(Vector2 positionOnCanvas, Type filterInputType, Type filterOutputType, bool onlyMultiInputs, string startingSearchString = "", System.Action<Symbol> overrideCreate = null)
    {
        // Scroll canvas to avoid symbol-browser close too edge

        var screenPos = _graphView.Canvas.TransformPosition(positionOnCanvas);
        var screenRect = ImRect.RectWithSize(screenPos, SymbolUi.Child.DefaultOpSize);
        screenRect.Expand(200 * _graphView.Canvas.Scale.X);
        var windowRect = ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize());
        var tooCloseToEdge = !windowRect.Contains(screenRect);

        var canvasPosition = _graphView.Canvas.InverseTransformPositionFloat(screenPos);
        if (tooCloseToEdge)
        {
            var canvasRect = ImRect.RectWithSize(canvasPosition, SymbolUi.Child.DefaultOpSize);
            canvasRect.Expand(400);
            _graphView.Canvas.FitAreaOnCanvas(canvasRect);
        }


        //_prepareCommand = prepareCommand;
        _overrideCreate = overrideCreate;
        IsOpen = true;
        PosOnCanvas = positionOnCanvas;
        _focusInputNextTime = true;
        _filter.FilterInputType = filterInputType;
        _filter.FilterOutputType = filterOutputType;
        _filter.SearchString = startingSearchString;
        _selectedSymbolUi = null;
        _filter.OnlyMultiInputs = onlyMultiInputs;
        _filter.UpdateIfNecessary(_components.NodeSelection, forceUpdate: true);
        DrawUtils.DisableImGuiKeyboardNavigation();

        if (_selectedSymbolUi == null && _filter.MatchingSymbolUis.Count > 0)
        {
            _selectedSymbolUi = _filter.MatchingSymbolUis[0];
        }
    }

    public void Draw()
    {
        var canvas = _graphView.Canvas;
        var nodeSelection = _components.NodeSelection;
        if (!IsOpen)
        {
            var hasFocus =  ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows);

            var anythingActive = ImGui.IsAnyItemActive();
            if (!hasFocus || anythingActive || !ImGui.IsKeyReleased((ImGuiKey)Key.Tab))
                return;

            if (nodeSelection.GetSelectedChildUis().Count() != 1)
            {
                ConnectionMaker.StartOperation(_graphView, "Add operator");
                    
                var screenPos = ImGui.GetIO().MousePos + new Vector2(-4, -20);
                var canvasPosition = canvas.InverseTransformPositionFloat(screenPos);
                    
                OpenAt(canvasPosition, null, null, false);
                    
                return;
            }

            var childUi = nodeSelection.GetSelectedChildUis().ToList()[0];
            {
                var instance = nodeSelection.GetInstanceForChildUi(childUi);
                if (instance == null)
                {
                    nodeSelection.Clear();
                    return;
                }
                    
                    
                var screenPos = canvas.TransformPosition(childUi.PosOnCanvas);
                var screenRect = ImRect.RectWithSize(screenPos, SymbolUi.Child.DefaultOpSize);
                screenRect.Expand(200 * canvas.Scale.X);
                var windowRect = ImRect.RectWithSize(ImGui.GetWindowPos(), ImGui.GetWindowSize());
                var tooCloseToEdge = !windowRect.Contains(screenRect);
                    
                var canvasPosition = canvas.InverseTransformPositionFloat(screenPos);
                if (tooCloseToEdge)
                {
                    var canvasRect = ImRect.RectWithSize(canvasPosition, SymbolUi.Child.DefaultOpSize);
                    canvasRect.Expand(400);
                    canvas.FitAreaOnCanvas(canvasRect);
                }
                    
                ConnectionMaker.OpenBrowserWithSingleSelection(_components, childUi, instance, this);
            }
            return;
        }

        FrameStats.Current.OpenedPopUpName = "SymbolBrowser";

        _filter.UpdateIfNecessary(_components.NodeSelection);

        ImGui.PushID(_uiId);
        {
            var posInWindow = canvas.ChildPosFromCanvas(PosOnCanvas);
            _posInScreen = canvas.TransformPosition(PosOnCanvas);
            _drawList = ImGui.GetWindowDrawList();

            ImGui.SetNextWindowFocus();

            var browserPositionInWindow = posInWindow + BrowserPositionOffset;
            var browserSize = ResultListSize;

            ClampPanelToCanvas(_graphView, ref browserPositionInWindow, ref browserSize);

            ImGui.SetCursorPos(browserPositionInWindow);

            DrawResultsList(browserSize);

            if (_selectedSymbolUi != null)
            {
                if (_filter.PresetFilterString != string.Empty && (_filter.WasUpdated || _selectedItemChanged))
                {
                    _matchingPresets.Clear();
                    var presetPool = VariationHandling.GetOrLoadVariations(_selectedSymbolUi.Symbol.Id);
                    if (presetPool != null)
                    {
                        _matchingPresets.AddRange(presetPool.AllVariations.Where(v => v.IsPreset && v.Title.Contains(_filter.PresetFilterString,
                                                                                          StringComparison.InvariantCultureIgnoreCase)));
                    }
                }

                if ( _filter.PresetFilterString != string.Empty)
                {
                    UiListHelpers.AdvanceSelectedItem(_matchingPresets, ref _selectedPreset, 0);
                    DrawPresetPanel(browserPositionInWindow, new Vector2(140, browserSize.Y));
                }
                else
                {
                    _selectedPreset = null;
                    DrawDescriptionPanel(browserPositionInWindow, browserSize);
                }
            }

            DrawSearchInput(posInWindow, _posInScreen, _size * T3Ui.UiScaleFactor);
        }

        ImGui.PopID();
    }
    #endregion

    private System.Action<Symbol> _overrideCreate = null;

    //private bool IsSearchingPresets => _filter.MatchingPresets.Count > 0;

    private void DrawSearchInput(Vector2 posInWindow, Vector2 posInScreen, Vector2 size)
    {
        if (_focusInputNextTime)
        {
            ImGui.SetKeyboardFocusHere();
            _focusInputNextTime = false;
            _selectedItemChanged = true;
        }

        ImGui.SetCursorPos(posInWindow);

        var padding = new Vector2(7, 6);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
        ImGui.SetNextItemWidth(size.X);

        ImGui.InputText("##symbolBrowserFilter", ref _filter.SearchString, 20);

        // Search input outline
        _drawList.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), UiColors.Gray);

        if (ImGui.IsKeyPressed((ImGuiKey)Key.Return))
        {
            if (_selectedSymbolUi != null)
            {
                CreateInstance(_selectedSymbolUi.Symbol);
            }
        }

        if (_filter.WasUpdated)
        {
            _selectedSymbolUi = _filter.MatchingSymbolUis.Count > 0
                                    ? _filter.MatchingSymbolUis[0]
                                    : null;
            _selectedItemChanged = true;
        }

        var clickedOutside = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered();
        var shouldCancelConnectionMaker = clickedOutside
                                          || ImGui.IsMouseClicked(ImGuiMouseButton.Right)
                                          || ImGui.IsKeyDown((ImGuiKey)Key.Esc);

        if (shouldCancelConnectionMaker)
        {
            //ConnectionMaker.AbortOperation();
            Cancel();
        }

        ImGui.PopStyleVar();

        if (!ImGui.IsItemActive())
            return;

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            PosOnCanvas += _graphView.Canvas.InverseTransformDirection(ImGui.GetIO().MouseDelta);
        }
    }

    private void Cancel()
    {
        ConnectionMaker.AbortOperation(_graphView);
        // if (_prepareCommand != null)
        // {
        //     _prepareCommand.Undo();
        //     _prepareCommand = null;
        // }

        Close();
    }

    private void Close()
    {
        DrawUtils.RestoreImGuiKeyboardNavigation();
        IsOpen = false;
        OnFocusRequested?.Invoke();
    }

    private void DrawResultsList(Vector2 size)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 10));
            
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundPopup.Rgba);

        if (ImGui.BeginChildFrame(999, size))
        {
            // if (_filter.PresetFilterString == null)
            // {
                if (ImGui.IsKeyReleased((ImGuiKey)Key.CursorDown))
                {
                    UiListHelpers.AdvanceSelectedItem(_filter.MatchingSymbolUis, ref _selectedSymbolUi, 1);
                    _selectedItemChanged = true;
                }
                else if (ImGui.IsKeyReleased((ImGuiKey)Key.CursorUp))
                {
                    UiListHelpers.AdvanceSelectedItem(_filter.MatchingSymbolUis, ref _selectedSymbolUi, -1);
                    _selectedItemChanged = true;
                }
            // }

            var gotAMatch = _filter.MatchingSymbolUis.Count > 0 && !_filter.MatchingSymbolUis.Contains(_selectedSymbolUi);
            if (gotAMatch)
                _selectedSymbolUi = _filter.MatchingSymbolUis[0];

            if ((_selectedSymbolUi == null && EditorSymbolPackage.AllSymbolUis.Any()))
                _selectedSymbolUi = EditorSymbolPackage.AllSymbolUis.First();

            PrintTypeFilter();

            var compositionOp = _components.CompositionInstance;
            var projectNamespace = "user." + compositionOp.Symbol.SymbolPackage.AssemblyInformation.Name + ".";
            var compositionNameSpace = "";
            var currentMainComposition = compositionOp;
            compositionNameSpace = currentMainComposition.Symbol.Namespace;

            foreach (var symbolUi in _filter.MatchingSymbolUis)
            {
                var symbolHash = symbolUi.Symbol.Id.GetHashCode();
                ImGui.PushID(symbolHash);
                {
                    var symbolNamespace = symbolUi.Symbol.Namespace;
                    var isRelevantNamespace = symbolNamespace.StartsWith("Lib.")
                                              || symbolNamespace.StartsWith("Types.")
                                              || symbolNamespace.StartsWith("Examples.Lib.")
                                              || symbolNamespace.StartsWith(projectNamespace)
                                              || symbolNamespace.StartsWith(compositionNameSpace);

                    var color = symbolUi.Symbol.OutputDefinitions.Count > 0
                                    ? TypeUiRegistry.GetPropertiesForType(symbolUi.Symbol.OutputDefinitions[0]?.ValueType).Color
                                    : UiColors.Gray;

                    if (!isRelevantNamespace)
                    {
                        color = color.Fade(0.4f);
                    }

                    ImGui.PushStyleColor(ImGuiCol.Header, ColorVariations.OperatorBackground.Apply(color).Rgba);

                    var hoverColor = ColorVariations.OperatorBackgroundHover.Apply(color).Rgba;
                    hoverColor.W = 0.1f;
                    ImGui.PushStyleColor(ImGuiCol.HeaderHovered, hoverColor);
                    //ImGui.PushStyleColor(ImGuiCol.HeaderActive, ColorVariations.OperatorInputZone.Apply(color).Rgba);
                    ImGui.PushStyleColor(ImGuiCol.Text, ColorVariations.OperatorLabel.Apply(color).Rgba);

                    var isSelected = symbolUi == _selectedSymbolUi;

                    _selectedItemChanged |= ImGui.Selectable($"##Selectable{symbolHash.ToString()}", isSelected);
                    //bool selectionChangedToThis = isSelected && _selectedItemChanged;

                    var isHovered = ImGui.IsItemHovered();
                    var hasMouseMoved = ImGui.GetIO().MouseDelta.LengthSquared() > 0;
                    if (hasMouseMoved && isHovered)
                    {
                        _selectedSymbolUi = symbolUi;
                        //_timeDescriptionSymbolUiLastHovered = DateTime.Now;
                        _selectedItemChanged = true;
                    }
                    else if (_selectedItemChanged && _selectedSymbolUi == symbolUi)
                    {
                        UiListHelpers.ScrollToMakeItemVisible();
                        _selectedItemChanged = false;
                    }

                    if (ImGui.IsItemActivated())
                    {
                        CreateInstance(symbolUi.Symbol);
                    }

                    ImGui.SameLine();

                    ImGui.TextUnformatted(symbolUi.Symbol.Name);
                    ImGui.SameLine();

                    if (!string.IsNullOrEmpty(symbolNamespace))
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Fade(0.5f).Rgba);
                        ImGui.Text(symbolNamespace);
                        ImGui.PopStyleColor();
                        ImGui.SameLine();
                    }

                    ImGui.NewLine();

                    ImGui.PopStyleColor(3);
                }
                ImGui.PopID();
            }
        }

        ImGui.EndChildFrame();

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
    }

    private void PrintTypeFilter()
    {
        if (_filter.FilterInputType == null && _filter.FilterOutputType == null)
            return;

        ImGui.PushFont(Fonts.FontSmall);

        var inputTypeName = _filter.FilterInputType != null
                                ? TypeNameRegistry.Entries[_filter.FilterInputType]
                                : string.Empty;

        var outputTypeName = _filter.FilterOutputType != null
                                 ? TypeNameRegistry.Entries[_filter.FilterOutputType]
                                 : string.Empty;

        var isMultiInput = _filter.OnlyMultiInputs ? "[..]" : "";

        var headerLabel = $"{inputTypeName}{isMultiInput}  -> {outputTypeName}";
        ImGui.TextDisabled(headerLabel);
        ImGui.PopFont();
    }

    private static void ClampPanelToCanvas(IGraphView graphView, ref Vector2 position, ref Vector2 size)
    {
        var maxXPos = position.X + size.X;
        var maxYPos = position.Y + size.Y;

        var windowSize = graphView.Canvas.WindowSize;

        var shouldShiftLeft = maxXPos > windowSize.X;
        var xPositionOffset = shouldShiftLeft ? windowSize.X - maxXPos : 0;

        var yOverflow = maxYPos > windowSize.Y;
        var yShrinkage = yOverflow ? windowSize.Y - maxYPos : 0;

        position.X += xPositionOffset;
        size.Y += yShrinkage;
    }

    private Variation _selectedPreset;

    private void DrawPresetPanel(Vector2 position, Vector2 size)
    {
        //size.X = 120;
        if (!TryFindValidPanelPosition(_graphView, ref position, size))
            return;

        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundPopup.Rgba);
        ImGui.SetCursorPos(position);
        if (ImGui.BeginChildFrame(998, size))
        {
            if (ImGui.IsKeyReleased((ImGuiKey)Key.CursorDown))
            {
                UiListHelpers.AdvanceSelectedItem(_matchingPresets, ref _selectedPreset, 1);
            }
            else if (ImGui.IsKeyReleased((ImGuiKey)Key.CursorUp))
            {
                UiListHelpers.AdvanceSelectedItem(_matchingPresets, ref _selectedPreset, -1);
            }

            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.One); // Padding between panels

            ImGui.PushFont(Fonts.FontLarge);
            ImGui.TextUnformatted(_selectedSymbolUi.Symbol.Name);
            ImGui.PopFont();
            ImGui.TextUnformatted("Presets");
            ImGui.Dummy(Vector2.One * 10);

            if (_matchingPresets.Count == 0)
            {
                CustomComponents.EmptyWindowMessage("No presets found");
            }
            else
            {
                foreach (var p in _matchingPresets)
                {
                    if (ImGui.Selectable(p.Title, _selectedPreset == p))
                    {
                        _selectedPreset = p;
                        CreateInstance(_selectedSymbolUi.Symbol);
                    }
                }
            }

            ImGui.PopStyleVar();
            ImGui.EndChildFrame();
        }
        ImGui.PopStyleColor();
            
    }

    private void DrawDescriptionPanel(Vector2 position, Vector2 size)
    {
        if (_selectedSymbolUi == null)
            return;

        var hasExamples = ExampleSymbolLinking.ExampleIdsForSymbolsId.TryGetValue(_selectedSymbolUi.Symbol.Id, out var examplesIds)
                          && examplesIds.Count > 0;

        var hasDescription = !string.IsNullOrEmpty(_selectedSymbolUi.Description);

        if (!hasExamples && !hasDescription)
            return;

        if (!TryFindValidPanelPosition(_graphView, ref position, size))
            return;

        ImGui.SetCursorPos(position);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.One); // Padding between panels
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundPopup.Rgba);

        if (ImGui.BeginChildFrame(998, size))
        {
            if (!string.IsNullOrEmpty(_selectedSymbolUi.Description))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
                ImGui.TextWrapped(_selectedSymbolUi.Description);
                ImGui.PopStyleColor();
            }

            if (hasExamples)
            {
                ImGui.Dummy(new Vector2(10, 10));
                ListExampleOperators(examplesIds);
            }

            ImGui.EndChildFrame();
        }

        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }

    private static bool TryFindValidPanelPosition(IGraphView graphView, ref Vector2 position, Vector2 size)
    {
        var maxXPos = position.X + size.X + ResultListSize.X;
        var wouldExceedWindowBounds = maxXPos >= graphView.Canvas.WindowSize.X;

        if (wouldExceedWindowBounds)
        {
            position.X -= size.X;

            //if it simply doesn't fit on screen, return
            if (position.X <= 0)
                return false;
        }
        else
        {
            position.X += size.X;
        }

        return true;
    }

    private static void ListExampleOperators(IEnumerable<Guid> exampleIds)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f * ImGui.GetStyle().Alpha);
        foreach (var guid in exampleIds)
        {
            if (!SymbolUiRegistry.TryGetSymbolUi(guid, out var symbolUi))
                return;
            
            const string label = "Example";
            UiElements.DrawExampleOperator(symbolUi, label);
        }

        ImGui.PopStyleVar();
    }

    private void CreateInstance(Symbol symbol)
    {
        if(_overrideCreate != null)
        {
            Close();
            _overrideCreate(symbol);
            return;
        }

        var commandsForUndo = new List<ICommand>();
        var parentOp = _components.CompositionInstance;
        var parentSymbol = parentOp.Symbol;
        var parentSymbolUi = parentSymbol.GetSymbolUi();

        var addSymbolChildCommand = new AddSymbolChildCommand(parentSymbol, symbol.Id) { PosOnCanvas = PosOnCanvas };
        commandsForUndo.Add(addSymbolChildCommand);
        addSymbolChildCommand.Do();

        // Select new node
        if (!parentSymbolUi.ChildUis.TryGetValue(addSymbolChildCommand.AddedChildId, out var newChildUi))
        {
            Log.Warning($"Unable to create new operator - failed to retrieve new child ui \"{addSymbolChildCommand.AddedChildId}\" " +
                        $"from parent symbol ui {parentSymbolUi.Symbol}");
            return;
        }
            
        var newSymbolChild = newChildUi.SymbolChild;
        var newInstance = _components.CompositionInstance.Children[newChildUi.Id];

        var presetPool = VariationHandling.GetOrLoadVariations(_selectedSymbolUi.Symbol.Id);
        if (presetPool != null && _selectedPreset != null)
        {
            presetPool.Apply(newInstance, _selectedPreset);
        }

        _components.NodeSelection.SetSelection(newChildUi, newInstance);

        // if (_prepareCommand != null)
        // {
        //     additionalCommands.Add(_prepareCommand);
        // }

        var tempConnections = ConnectionMaker.GetTempConnectionsFor(_graphView);

        foreach (var c in tempConnections)
        {
            switch (c.GetStatus())
            {
                case ConnectionMaker.TempConnection.Status.SourceIsDraftNode:
                    var outputDef = newSymbolChild.Symbol.GetOutputMatchingType(c.ConnectionType);
                    if (outputDef == null)
                    {
                        Log.Error("Failed to find matching output connection type " + c.ConnectionType);
                        return;
                    }

                    var newConnectionToSource = new Symbol.Connection(sourceParentOrChildId: newSymbolChild.Id,
                                                                      sourceSlotId: outputDef.Id,
                                                                      targetParentOrChildId: c.TargetParentOrChildId,
                                                                      targetSlotId: c.TargetSlotId);
                    var addConnectionCommand = new AddConnectionCommand(parentSymbolUi.Symbol, newConnectionToSource, c.MultiInputIndex);
                    addConnectionCommand.Do();
                    commandsForUndo.Add(addConnectionCommand);
                    break;

                case ConnectionMaker.TempConnection.Status.TargetIsDraftNode:
                    var inputDef = newSymbolChild.Symbol.GetInputMatchingType(c.ConnectionType);
                    if (inputDef == null)
                    {
                        Log.Warning("Failed to complete node creation");
                        return;
                    }

                    var newConnectionToInput = new Symbol.Connection(sourceParentOrChildId: c.SourceParentOrChildId,
                                                                     sourceSlotId: c.SourceSlotId,
                                                                     targetParentOrChildId: newSymbolChild.Id,
                                                                     targetSlotId: inputDef.Id);
                    var connectionCommand = new AddConnectionCommand(parentSymbolUi.Symbol, newConnectionToInput, c.MultiInputIndex);
                    connectionCommand.Do();
                    commandsForUndo.Add(connectionCommand);
                    break;
            }
        }

        // var newCommand = new MacroCommand("Insert Op", commands);
        // UndoRedoStack.Add(newCommand);
        ConnectionMaker.CompleteOperation(_graphView, commandsForUndo, "Insert Op " + newChildUi.SymbolChild.ReadableName);
        ParameterPopUp.NodeIdRequestedForParameterWindowActivation = newSymbolChild.Id;
        Close();
    }

    private readonly SymbolFilter _filter = new();

    public Vector2 PosOnCanvas { get; private set; }
    public Vector2 OutputPositionOnScreen => _posInScreen + _size;
    public bool IsOpen;

    private readonly Vector2 _size = SymbolUi.Child.DefaultOpSize;
    private static Vector2 BrowserPositionOffset => new(0, 40);

    private bool _focusInputNextTime;
    private Vector2 _posInScreen;
    private ImDrawListPtr _drawList;
    private bool _selectedItemChanged;

    private static Vector2 ResultListSize => new Vector2(250, 300) * new Vector2(2,1) * T3Ui.UiScaleFactor;
    private readonly Vector4 _namespaceColor = new Color(1, 1, 1, 0.4f);

    private SymbolUi _selectedSymbolUi;
    private static readonly int _uiId = "DraftNode".GetHashCode();
    private readonly List<Variation> _matchingPresets = new();
    public event Action OnFocusRequested;
}