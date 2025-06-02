#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Model;
using T3.Core.Utils;
using T3.Editor.Gui.MagGraph.Model;
using T3.Editor.Gui.MagGraph.States;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.InputsAndTypes;
using T3.SystemUi;

namespace T3.Editor.Gui.MagGraph.Interaction;

internal static class PlaceHolderUi
{
    internal static void Open(GraphUiContext context, MagGraphItem placeholderItem, 
                              MagGraphItem.Directions connectionOrientation = MagGraphItem.Directions.Horizontal,
                              Type? inputFilter = null, Type? outputFilter = null)
    {
        _selectedSymbolUi = null;
        _focusInputNextTime = true;
        Filter.FilterInputType = inputFilter;
        Filter.FilterOutputType = outputFilter;
        Filter.WasUpdated = true;
        Filter.SearchString = string.Empty;
        Filter.UpdateIfNecessary(context.Selector, forceUpdate: true);
        _placeholderItem = placeholderItem;
        _connectionOrientation = connectionOrientation;
        WindowContentExtend.GetLastAndReset();
        SymbolBrowsing.Reset();
    }

    internal static void Reset()
    {
        Filter.Reset();
        _placeholderItem = null;
        _selectedSymbolUi = null; // clear references
    }

    internal static UiResults Draw(GraphUiContext context, out SymbolUi? selectedUi)
    {
        selectedUi = null;

        var drawList = ImGui.GetWindowDrawList();
        var uiResult = UiResults.None;

        // Might have been closed from input
        if (_placeholderItem == null)
            return uiResult;

        FrameStats.Current.OpenedPopUpName = "SymbolBrowser";

        Filter.UpdateIfNecessary(context.Selector);

        uiResult |= DrawSearchInput(context, drawList);

        var pMin = context.Canvas.TransformPosition(_placeholderItem.PosOnCanvas);
        var pMax = context.Canvas.TransformPosition(_placeholderItem.Area.Max);
        uiResult |= DrawResultsList(context, new ImRect(pMin, pMax), Filter, _connectionOrientation);

        var clickedOutsidePlaceholder = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !_placeholderAreaOnScreen.Contains(ImGui.GetMousePos());
        if (clickedOutsidePlaceholder && uiResult.HasFlag(UiResults.ClickedOutside))
        {
            uiResult |= UiResults.Cancel;
        }

        // TODO: Implement preset search
        // if (_selectedSymbolUi != null)
        // {
        //     if (Filter.PresetFilterString != string.Empty && (Filter.WasUpdated || _selectedItemChanged))
        //     {
        //         
        //     }
        // }

        if (_focusInputNextTime)
        {
            ImGui.SetKeyboardFocusHere();
            _focusInputNextTime = false;
        }

        selectedUi = _selectedSymbolUi;
        return uiResult;
    }



    private static UiResults DrawSearchInput(GraphUiContext context, ImDrawListPtr drawList)
    {
        var uiResult = UiResults.None;
        Debug.Assert(_placeholderItem != null);

        var canvasScale = context.Canvas.Scale.X;
        var item = _placeholderItem;
        var pMin = context.Canvas.TransformPosition(item.PosOnCanvas);
        var pMax = context.Canvas.TransformPosition(item.PosOnCanvas + item.Size);
        var pMinVisible = pMin;
        var pMaxVisible = pMax;

        _placeholderAreaOnScreen = ImRect.RectBetweenPoints(pMin, pMax);

        // Background and Outline
        drawList.AddRectFilled(pMinVisible + Vector2.One * canvasScale, pMaxVisible - Vector2.One,
                               UiColors.BackgroundFull, 2 * canvasScale);

        if (_focusInputNextTime)
        {
            ImGui.SetKeyboardFocusHere();
            _focusInputNextTime = false;
            uiResult |= UiResults.SelectionChanged;
        }

        var labelPos = new Vector2(pMin.X,
                                   (pMin.Y + pMax.Y) / 2 - ImGui.GetFrameHeight() / 2);

        var posInWindow = labelPos - ImGui.GetWindowPos();

        ImGui.SetCursorPos(posInWindow);

        var favoriteGroup = SymbolBrowsing.IsFilterActive ? SymbolBrowsing.FilterString : string.Empty;
        
        if (string.IsNullOrEmpty(favoriteGroup))
        {
            var padding = new Vector2(9, 3);
            if (string.IsNullOrEmpty(Filter.SearchString))
            {
                drawList.AddText(labelPos + padding, UiColors.TextDisabled, "search...");
            }

            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, padding);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Color.Transparent.Rgba);
            ImGui.SetNextItemWidth(item.Size.X);
            ImGui.InputText("##symbolBrowserFilter",
                            ref Filter.SearchString,
                            20, ImGuiInputTextFlags.AutoSelectAll);

            ImGui.PopStyleColor();
        }
        else
        {
            if (!string.IsNullOrEmpty( favoriteGroup))
            {
                ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(5, 5));
                ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10);
                if (ImGui.Button(favoriteGroup + "  ×"))
                {
                    SymbolBrowsing.Reset();
                }

                ImGui.PopStyleColor();
            }
        }

        if (ImGui.IsKeyPressed((ImGuiKey)Key.Return))
        {
            if (_selectedSymbolUi != null)
            {
                uiResult |= UiResults.Create;
            }
        }

        if (Filter.WasUpdated)
        {
            _selectedSymbolUi = Filter.MatchingSymbolUis.Count > 0
                                    ? Filter.MatchingSymbolUis[0]
                                    : null;
            uiResult |= UiResults.SelectionChanged;
        }

        var clickedOutside = false; //ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered();
        var shouldCancelConnectionMaker = clickedOutside
                                          //|| ImGui.IsMouseClicked(ImGuiMouseButton.Right)
                                          || ImGui.IsKeyDown((ImGuiKey)Key.Esc);

        if (shouldCancelConnectionMaker)
        {
            uiResult |= UiResults.Cancel;
            //Cancel(context);
        }

        ImGui.PopStyleVar();

        if (!ImGui.IsItemActive())
            return uiResult;

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _placeholderItem.PosOnCanvas += context.Canvas.InverseTransformDirection(ImGui.GetIO().MouseDelta);
        }

        return uiResult;
    }

    private static UiResults DrawResultsList(GraphUiContext context, ImRect screenItemArea, SymbolFilter filter, MagGraphItem.Directions orientation)
    {
        var result = UiResults.None;
        var popUpSize = new Vector2(150, 235) * T3Ui.UiScaleFactor;
        var windowSize = ImGui.GetWindowSize();
        var windowPos = ImGui.GetWindowPos();
        Vector2 resultPosOnScreen = new Vector2(screenItemArea.Min.X, screenItemArea.Max.Y + 3);
        if (orientation == MagGraphItem.Directions.Vertical)
        {
            var y = screenItemArea.GetCenter().Y - 0.1f * popUpSize.Y;
            resultPosOnScreen.Y = y.Clamp(windowPos.Y + 10, windowSize.Y + windowPos.Y - popUpSize.Y - 10);
            resultPosOnScreen.X = screenItemArea.Max.X.Clamp(windowPos.X + 10,
                                                             windowPos.X + windowSize.X - popUpSize.X - 10);
        }


        var resultPosOnWindow = resultPosOnScreen - ImGui.GetWindowPos();

        ImGui.SetCursorPos(resultPosOnWindow);

        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 14);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(6, 6));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3, 6));

        ImGui.PushStyleColor(ImGuiCol.ChildBg, UiColors.BackgroundFull.Fade(0.8f).Rgba);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Color.Transparent.Rgba);

        var last = WindowContentExtend.GetLastAndReset() 
                   + ImGui.GetStyle().WindowPadding * 2
                   + new Vector2(10,3);
        last.Y = last.Y.Clamp(0,300);
        
        var resultAreaOnScreen = ImRect.RectWithSize(resultPosOnScreen, last);

        //ImGui.SetNextWindowSize(new Vector2(200,200));
        if (ImGui.BeginChild(999, last, true,
                             ImGuiWindowFlags.AlwaysUseWindowPadding
                             | ImGuiWindowFlags.NoResize
                            ))
        {
            FrameStats.Current.OpenedPopupHovered = ImGui.IsWindowHovered();
            if (!string.IsNullOrEmpty(filter.SearchString)
                || filter.FilterInputType != null
                || filter.FilterOutputType != null)
            {
                result |= DrawSearchResultEntries(context, filter);
            }
            else
            {
                result |= SymbolBrowsing.Draw(context);
                

                //result |= DrawGroups(context, filter);
            }
        }

        ImGui.EndChild();

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(4);
        var wasClickedOutside = ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !resultAreaOnScreen.Contains(ImGui.GetMousePos());
        if (wasClickedOutside)
        {
            result |= UiResults.ClickedOutside;
        }

        return result;
    }

    
    private static void PrintTypeFilter(SymbolFilter filter)
    {
        if (filter.FilterInputType == null && filter.FilterOutputType == null)
            return;

        ImGui.PushFont(Fonts.FontSmall);

        var inputTypeName = filter.FilterInputType != null
                                ? TypeNameRegistry.Entries[filter.FilterInputType]
                                : string.Empty;

        var outputTypeName = filter.FilterOutputType != null
                                 ? TypeNameRegistry.Entries[filter.FilterOutputType]
                                 : string.Empty;

        var isMultiInput = filter.OnlyMultiInputs ? "[..]" : "";

        var headerLabel = $"{inputTypeName}{isMultiInput}  -> {outputTypeName}";
        ImGui.TextDisabled(headerLabel);
        ImGui.PopFont();
    }

    private static UiResults DrawSearchResultEntries(GraphUiContext context, SymbolFilter filter)
    {
        var result = UiResults.None;
        if (ImGui.IsKeyReleased((ImGuiKey)Key.CursorDown))
        {
            UiListHelpers.AdvanceSelectedItem(filter.MatchingSymbolUis!, ref _selectedSymbolUi, 1);
            result = UiResults.SelectionChanged;
        }
        else if (ImGui.IsKeyReleased((ImGuiKey)Key.CursorUp))
        {
            UiListHelpers.AdvanceSelectedItem(filter.MatchingSymbolUis!, ref _selectedSymbolUi, -1);
            result = UiResults.SelectionChanged;
        }

        var gotAMatch = filter.MatchingSymbolUis.Count > 0
                        && (_selectedSymbolUi != null && !filter.MatchingSymbolUis.Contains(_selectedSymbolUi));
        if (gotAMatch)
            _selectedSymbolUi = filter.MatchingSymbolUis[0];

        if (_selectedSymbolUi == null && EditorSymbolPackage.AllSymbolUis.Any())
            _selectedSymbolUi = EditorSymbolPackage.AllSymbolUis.First();

        foreach (var symbolUi in filter.MatchingSymbolUis)
        {
            result |= DrawSymbolUiEntry(context, symbolUi);
            WindowContentExtend.ExtendToLastItem();
        }

        return result;
    }

    internal static UiResults DrawSymbolUiEntry(GraphUiContext context, SymbolUi symbolUi)
    {
        var result = UiResults.None;
        var symbolHash = symbolUi.Symbol.Id.GetHashCode();
        ImGui.PushID(symbolHash);
        {
            var symbolNamespace = symbolUi.Symbol.Namespace;
            var isRelevantNamespace = IsRelevantNamespace(context, symbolNamespace);

            var color = symbolUi.Symbol.OutputDefinitions.Count > 0
                            ? TypeUiRegistry.GetPropertiesForType(symbolUi.Symbol.OutputDefinitions[0]?.ValueType).Color
                            : UiColors.Gray;

            if (!isRelevantNamespace)
            {
                color = color.Fade(0.4f);
            }

            ImGui.PushStyleColor(ImGuiCol.Header, ColorVariations.OperatorBackground.Apply(color).Rgba);

            var hoverColor = ColorVariations.OperatorBackgroundHover.Apply(color).Rgba;
            hoverColor.W = 0.3f;
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, hoverColor);
            ImGui.PushStyleColor(ImGuiCol.Text, ColorVariations.OperatorLabel.Apply(color).Rgba);

            var isSelected = symbolUi == _selectedSymbolUi;

            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 14);
            ImGui.SetNextItemWidth(20);
            var size = ImGui.CalcTextSize(symbolUi.Symbol.Name);
            if (ImGui.Selectable($"##Selectable{symbolHash.ToString()}", 
                                 isSelected, 
                                 ImGuiSelectableFlags.None,
                                 new Vector2(size.X,0)))
            {
                result |= UiResults.Create;
                _selectedSymbolUi = symbolUi;
            }

            ImGui.PopStyleVar();

            // var dl = ImGui.GetForegroundDrawList();
            // dl.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), Color.Green);
            var isHovered = ImGui.IsItemHovered();
            if (isHovered)
            {
                ImGui.SetNextWindowSize(new Vector2(300, 0));
                ImGui.BeginTooltip();
                OperatorHelp.DrawHelpSummary(symbolUi, false);
                ImGui.EndTooltip();
            }

            //ImGui.set
            ImGui.SameLine(ImGui.GetItemRectMin().X - ImGui.GetWindowPos().X);
            ImGui.TextUnformatted(symbolUi.Symbol.Name);
            ImGui.PopStyleColor(3);
        }
        ImGui.PopID();
        return result;
    }

    private static bool IsRelevantNamespace(GraphUiContext context, string symbolNamespace)
    {
        var projectNamespace = "user." + context.CompositionInstance.Symbol.SymbolPackage.AssemblyInformation.Name + ".";
        var compositionNameSpace = context.CompositionInstance.Symbol.Namespace;

        var isRelevantNamespace = symbolNamespace.StartsWith("Lib.")
                                  || symbolNamespace.StartsWith("Types.")
                                  || symbolNamespace.StartsWith("Examples.Lib.")
                                  || symbolNamespace.StartsWith(projectNamespace)
                                  || symbolNamespace.StartsWith(compositionNameSpace);
        return isRelevantNamespace;
    }
    
    private static bool _focusInputNextTime = true;
    private static SymbolUi? _selectedSymbolUi;
    private static ImRect _placeholderAreaOnScreen;
    internal static readonly SymbolFilter Filter = new();
    private static MagGraphItem? _placeholderItem;

    private static MagGraphItem.Directions _connectionOrientation = MagGraphItem.Directions.Horizontal;

    [Flags]
    internal enum UiResults
    {
        None = 1<<1,
        SelectionChanged = 1<<2,
        FilterChanged = 1<<3,
        Create = 1<<4,
        Cancel = 1<<5,
        ClickedOutside = 1<<6,
    }
}