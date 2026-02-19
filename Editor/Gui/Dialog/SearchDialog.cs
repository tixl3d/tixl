#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;
using T3.Editor.UiModel.ProjectHandling;
using T3.SystemUi;

namespace T3.Editor.Gui.Dialog;

internal sealed class SearchDialog : ModalDialog
{
    public SearchDialog()
    {
        DialogSize = new Vector2(500, 300);
        Padding = 4;
    }

    public void Draw()
    {
        if (BeginDialog("Search"))
        {
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);

            if (!_isOpen)
            {
                _justOpened = true;
                _isOpen = true;
            }
            else
            {
                _justOpened = false;
            }

            if (_justOpened)
            {
                ImGui.SetKeyboardFocusHere();
            }

            var needsUpdate = _justOpened;
            FormInputs.SetIndentToLeft();

            FormInputs.SetWidth(0.7f);
            needsUpdate |= FormInputs.AddStringInput("##searchInput", ref _searchString, "Search", null, null);
            ImGui.SameLine(0,10);
            
            FormInputs.SetWidth(1f);
            needsUpdate |= FormInputs.AddEnumDropdown(ref _searchMode, "");
            FormInputs.ResetWidth();
            FormInputs.AddVerticalSpace();

            if (needsUpdate)
            {
                UpdateResults();
            }

            var clickedOutside = ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                                 && !ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            if (ImGui.IsKeyReleased(ImGuiKey.Enter) || ImGui.IsKeyReleased(ImGuiKey.Escape) || clickedOutside)
            {
                ImGui.CloseCurrentPopup();
            }

            DrawResultsList();
            ImGui.PopStyleVar();
            EndDialogContent();
        }
        else
        {
            _isOpen = false;
        }

        EndDialog();
    }

    private bool _isOpen;

    private void DrawResultsList()
    {
        var size = ImGui.GetContentRegionAvail();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(5, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10, 10));

        var matchingItems = _matchingItems;

        if (ImGui.BeginChildFrame(999, size))
        {
            if (ImGui.IsKeyReleased((ImGuiKey)Key.CursorDown))
            {
                UiListHelpers.AdvanceSelectedItem(matchingItems!, ref _selectedInstance!, 1);
                _selectedItemChanged = true;
                var index = matchingItems.IndexOf(_selectedInstance);
                if (index == 0)
                    ImGui.SetScrollY(0);
            }
            else if (ImGui.IsKeyReleased((ImGuiKey)Key.CursorUp))
            {
                UiListHelpers.AdvanceSelectedItem(matchingItems!, ref _selectedInstance!, -1);
                _selectedItemChanged = true;

                var index = matchingItems.IndexOf(_selectedInstance);

                if (index * ImGui.GetTextLineHeight() > ImGui.GetScrollY() + ImGui.GetContentRegionAvail().Y)
                {
                    Log.Debug("Would scroll down");
                    ImGui.SetScrollY(ImGui.GetScrollY() + ImGui.GetContentRegionAvail().Y);
                }
            }

            unsafe
            {
                var clipperData = new ImGuiListClipper();
                var listClipperPtr = new ImGuiListClipperPtr(&clipperData);

                listClipperPtr.Begin(matchingItems.Count, ImGui.GetTextLineHeight());
                while (listClipperPtr.Step())
                {
                    for (var i = listClipperPtr.DisplayStart; i < listClipperPtr.DisplayEnd; ++i)
                    {
                        if (i < 0 || i >= matchingItems.Count)
                            continue;

                        DrawItem(matchingItems[i]);
                    }
                }

                listClipperPtr.End();
            }
        }

        ImGui.EndChildFrame();

        ImGui.PopStyleVar(2);
    }

    private void DrawItem(SearchResult item)
    {
        var components = item.GraphCanvas;
        if (components == null)
            return;

        var symbolHash = item.Id.GetHashCode();
        ImGui.PushID(symbolHash);
        {
            var instance = item.Instance;
            if (item.Annotation == null)
            {
                var symbolNamespace = instance.Symbol.Namespace;
                var isRelevantNamespace = symbolNamespace.StartsWith("Lib.")
                                          || symbolNamespace.StartsWith("examples.lib.");

                var color = instance.Symbol.OutputDefinitions.Count > 0
                                ? TypeUiRegistry.GetPropertiesForType(instance.Symbol.OutputDefinitions[0]?.ValueType).Color
                                : UiColors.Gray;

                if (!isRelevantNamespace)
                {
                    color = color.Fade(0.4f);
                }

                ImGui.PushStyleColor(ImGuiCol.Header, ColorVariations.OperatorBackground.Apply(color).Rgba);

                var hoverColor = ColorVariations.OperatorBackgroundHover.Apply(color).Rgba;
                hoverColor.W = 0.1f;
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, hoverColor);
                ImGui.PushStyleColor(ImGuiCol.Text, ColorVariations.OperatorLabel.Apply(color).Rgba);
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(2, 4));

                var isSelected = item == _selectedInstance;
                ImGui.PushID(item.Id.GetHashCode());
                var hasBeenClicked = ImGui.Selectable("##Selectable", isSelected);
                ImGui.PopID();
                _selectedItemChanged |= hasBeenClicked;

                var path = instance.InstancePath;
                var readablePath = string.Join(" / ", components.OpenedProject.Structure.GetReadableInstancePath(path, false));

                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    components.GraphView.OpenAndFocusInstance(path);
                    _selectedInstance = item;
                    _selectedItemChanged = false;
                }
                else if (_selectedItemChanged && _selectedInstance == item)
                {
                    UiListHelpers.ScrollToMakeItemVisible();
                    components.GraphView.OpenAndFocusInstance(path);
                    _selectedItemChanged = false;
                }

                ImGui.SameLine();

                ImGui.TextUnformatted(instance.Symbol.Name);
                ImGui.SameLine(0, 10);

                CustomComponents.StylizedText(readablePath, Fonts.FontNormal, UiColors.Gray.Fade(0.5f));
                
                ImGui.PopStyleVar();
                ImGui.PopStyleColor(3);
            }
            else if (item.Annotation != null) 
            {
                var isSelected = item == _selectedInstance;
                var hasBeenClicked = ImGui.Selectable($"##Selectable{symbolHash.ToString()}", isSelected);
                
                _selectedItemChanged |= hasBeenClicked;

                var activate = false;
                if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    _selectedInstance = item;
                    activate = true;
                }
                else if (_selectedItemChanged && _selectedInstance == item)
                {
                    UiListHelpers.ScrollToMakeItemVisible();
                    _selectedItemChanged = false;
                    activate = true;
                }

                if (activate)
                {
                    components.GraphView.OpenAndFocusAnnotation(item.Instance.InstancePath, item.Annotation.Id);
                    _selectedInstance = item;
                }

                ImGui.SameLine(0,2);
                ImGui.TextUnformatted(item.Name);
                
                ImGui.SameLine(0,10);
                CustomComponents.StylizedText("(Annotation)", Fonts.FontNormal, UiColors.TextMuted);

            }
        }

        ImGui.PopID();
    }

    private void UpdateResults()
    {
        _matchingItems.Clear();

        var components = ProjectView.Focused;
        if (components == null)
            return;

        // Show navigation / selection history if not searching
        if (string.IsNullOrEmpty(_searchString))
        {
            var previousInstances = components.NavigationHistory
                                              .GetPreviouslySelectedInstances()
                                              .Select(instance => new SearchResult(instance));

            _matchingItems.AddRange(previousInstances);

            if (_matchingItems.Count > 0)
                _selectedInstance = _matchingItems[0];

            return;
        }

        //
        var compositionOp = components.CompositionInstance;
        var composition = _searchMode switch
                              {
                                  SearchModes.Global             => components.RootInstance,
                                  SearchModes.Local              => compositionOp,
                                  SearchModes.LocalAndInChildren => compositionOp,
                                  _                              => throw new ArgumentOutOfRangeException()
                              };

        if (composition == null)
            return;

        if (!string.IsNullOrEmpty(_searchString))
        {
            FindAllChildren(composition,
                            instance =>
                            {
                                _matchingItems.Add(new SearchResult(instance));
                            },
                            (compositionInstance, annotation) =>
                            {
                                _matchingItems.Add(new SearchResult(compositionInstance, annotation));
                            });
        }
        
        
        
        _matchingItems.Sort((foundA, foundB) =>
                            {
                                // 1. Sort by IsAnnotation (true first)
                                if ((foundB.Annotation != null).CompareTo(foundA.Annotation !=null) != 0)
                                {
                                    return (foundB.Annotation != null).CompareTo(foundA.Annotation !=null);
                                }

                                // 2. If IsAnnotation is the same, sort by Name alphabetically
                                return string.Compare(foundA.Name, foundB.Name, StringComparison.Ordinal);
                            });

        //_matchingItems.Sort((foundA, foundB) => string.Compare(foundA.Name, foundB.Name, StringComparison.Ordinal));
    }

    private void FindAllChildren(Instance composition, Action<Instance> instanceCallback, Action<Instance, Annotation> annotationCallback)
    {
        var symbolUi = composition.GetSymbolUi();
        
        foreach (var annotation in symbolUi.Annotations.Values)
        {
            if (annotation.Label.Contains(_searchString, StringComparison.InvariantCultureIgnoreCase) ||
                annotation.Title.Contains(_searchString, StringComparison.InvariantCultureIgnoreCase))
                annotationCallback(composition, annotation);
        }

        foreach (var child in composition.Children.Values)
        {
            if (child.Symbol.Name.Contains(_searchString, StringComparison.InvariantCultureIgnoreCase))
            {
                instanceCallback(child);
            }
            
            if (_searchMode == SearchModes.Local)
                continue;


            FindAllChildren(child, instanceCallback, annotationCallback);
        }
    }

    private readonly List<SearchResult> _matchingItems = new();
    private bool _justOpened;
    private static string _searchString = string.Empty;
    private SearchResult? _selectedInstance;
    private bool _selectedItemChanged;
    private SearchModes _searchMode = SearchModes.Local;

    private sealed class SearchResult
    {
        public SearchResult(Instance instance)
        {
            Instance = instance;
            GraphCanvas = ProjectView.Focused;
            Id = instance.SymbolChildId;
        }

        public SearchResult(Instance compositionInstance, Annotation annotation)
        {
            Instance = compositionInstance;
            Annotation = annotation;
            GraphCanvas = ProjectView.Focused;
            Id = annotation.Id;
        }

        public readonly Guid Id;
        public readonly Instance Instance;
        public readonly Annotation? Annotation;
        public readonly ProjectView? GraphCanvas;

        public string Name => Annotation != null 
                                  ? Annotation?.Label ?? string.Empty 
                                  : Instance.Symbol.Name;
    }

    private enum SearchModes
    {
        Local,
        LocalAndInChildren,
        Global,
    }
}