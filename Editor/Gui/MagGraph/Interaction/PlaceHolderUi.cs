#nullable enable

using System;
using System.Diagnostics;
using System.Linq;

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
    internal static void Open(
        GraphUiContext context,
        MagGraphItem placeholderItem,
        MagGraphItem.Directions connectionOrientation = MagGraphItem.Directions.Horizontal,
        Type? inputFilter = null,
        Type? outputFilter = null)
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

        _rowHeight = 0;
    }

    internal static void Reset()
    {
        Filter.Reset();
        _placeholderItem = null;
        _selectedSymbolUi = null;
        _rowHeight = 0;
        _focusInputNextTime = false;
    }

    internal static UiResults Draw(GraphUiContext context, out SymbolUi? selectedUi)
    {
        selectedUi = null;

        var drawList = ImGui.GetWindowDrawList();
        var uiResult = UiResults.None;

        if (_placeholderItem == null)
            return uiResult;

        FrameStats.Current.OpenedPopUpName = "SymbolBrowser";

        Filter.UpdateIfNecessary(context.Selector);

        uiResult |= DrawSearchInput(context, drawList);

        var pMin = context.View.TransformPosition(_placeholderItem.PosOnCanvas);
        var pMax = context.View.TransformPosition(_placeholderItem.Area.Max);

        uiResult |= DrawResultsList(context, new ImRect(pMin, pMax), Filter, _connectionOrientation);

        // Click outside = mouse click that is NOT in placeholder rect and NOT in results rect.
        var clickedOutsidePlaceholder = ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                                        && !_placeholderAreaOnScreen.Contains(ImGui.GetMousePos());
        if (clickedOutsidePlaceholder && uiResult.HasFlag(UiResults.ClickedOutside))
        {
            uiResult |= UiResults.Cancel;
        }

        if (_focusInputNextTime)
        {
            ImGui.SetKeyboardFocusHere();
            _focusInputNextTime = false;
        }

        selectedUi = _selectedSymbolUi;
        return uiResult;
    }
    
    // TODO: Implement preset search
    // if (_selectedSymbolUi != null)
    // {
    //     if (Filter.PresetFilterString != string.Empty && (Filter.WasUpdated || _selectedItemChanged))
    //     {
    //
    //     }
    // }
    
    private static UiResults DrawSearchInput(GraphUiContext context, ImDrawListPtr drawList)
    {
        var uiResult = UiResults.None;
        Debug.Assert(_placeholderItem != null);

        var canvasScale = context.View.Scale.X;

        var item = _placeholderItem;
        var pMin = context.View.TransformPosition(item.PosOnCanvas);
        var pMax = context.View.TransformPosition(item.PosOnCanvas + item.Size);

        var pMinVisible = pMin;
        var pMaxVisible = pMax;

        _placeholderAreaOnScreen = ImRect.RectBetweenPoints(pMin, pMax);

        // Background and Outline
        drawList.AddRectFilled(pMinVisible + Vector2.One * canvasScale,
                               pMaxVisible - Vector2.One,
                               UiColors.BackgroundFull,
                               2 * canvasScale);

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

            ImGui.PushID("SymbolBrowserSearch");
            ImGui.InputText("##symbolBrowserFilter",
                            ref Filter.SearchString,
                            20,
                            ImGuiInputTextFlags.AutoSelectAll);
            ImGui.PopID();

            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
        }
        else
        {
            ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(5, 5));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10);
            ImGui.PushStyleColor(ImGuiCol.Button, Color.Transparent.Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, UiColors.ForegroundFull.Fade(0.1f).Rgba);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, UiColors.ForegroundFull.Fade(0.2f).Rgba);

            if (ImGui.Button(favoriteGroup + " ×"))
            {
                SymbolBrowsing.Reset();
            }

            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar();
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
            _rowHeight = 0;
            uiResult |= UiResults.SelectionChanged;
        }

        // Preserve Esc-based cancel logic with shouldCancelConnectionMaker.
        var clickedOutside = false; // TODO: wire this up if window-hover based cancel is reintroduced
        var shouldCancelConnectionMaker = clickedOutside
                                          // ImGui.IsMouseClicked(ImGuiMouseButton.Right)
                                          || ImGui.IsKeyDown((ImGuiKey)Key.Esc);

        if (shouldCancelConnectionMaker)
        {
            uiResult |= UiResults.Cancel;
            //Cancel(context);    // TODO: Implement cancel behavior if/when needed
        }

        if (!ImGui.IsItemActive())
            return uiResult;

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            _placeholderItem.PosOnCanvas += context.View.InverseTransformDirection(ImGui.GetIO().MouseDelta);
        }

        return uiResult;
    }

    private static UiResults DrawResultsList(
        GraphUiContext context,
        ImRect screenItemArea,
        SymbolFilter filter,
        MagGraphItem.Directions orientation)
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

        // Base size from previous content.
        var last = WindowContentExtend.GetLastAndReset()
                   + ImGui.GetStyle().WindowPadding * 2
                   + new Vector2(10, 3);

        // Target: show up to 12 rows on first open if we have enough items.
        const int targetVisibleRows = 12;
        if (filter.MatchingSymbolUis.Count >= targetVisibleRows)
        {
            // Ensure _rowHeight is initialized (approximate if needed).
            if (_rowHeight <= 0)
            {
                var lineHeight = ImGui.GetTextLineHeightWithSpacing();
                _rowHeight = lineHeight;
            }

            var desiredHeight = _rowHeight * targetVisibleRows;
            // Add top/bottom padding similar to your existing layout.
            desiredHeight += ImGui.GetStyle().WindowPadding.Y * 2;

            if (desiredHeight > last.Y)
            {
                last.Y = desiredHeight;
            }
        }

        last.Y = last.Y.Clamp(0, 300);

        var resultAreaOnScreen = ImRect.RectWithSize(resultPosOnScreen, last);

        bool childOpen = ImGui.BeginChild(
            999,
            last,
            true,
            ImGuiWindowFlags.AlwaysUseWindowPadding
            | ImGuiWindowFlags.NoResize);

        if (childOpen)
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
            }
        }
        ImGui.EndChild();

        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(4);

        var wasClickedOutside = ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                                && !resultAreaOnScreen.Contains(ImGui.GetMousePos());
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
        var headerLabel = $"{inputTypeName}{isMultiInput} -> {outputTypeName}";
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

        // --- ImGuiListClipper integration (only when child is visible) ---
        var count = filter.MatchingSymbolUis.Count;
        if (count > 0)
        {
            unsafe
            {
                // Measure one row height once (symbol name text height).
                if (_rowHeight <= 0)
                {
                    var size = ImGui.CalcTextSize(filter.MatchingSymbolUis[0].Symbol.Name);
                    // Add padding similar to the selectable's visual height.
                    _rowHeight = size.Y + ImGui.GetStyle().FramePadding.Y * 2;
                }

                ImGuiListClipperPtr clipper =
                    new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());

                clipper.Begin(count, _rowHeight);

                while (clipper.Step())
                {
                    for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        var symbolUi = filter.MatchingSymbolUis[i];
                        result |= DrawSymbolUiEntry(context, symbolUi);
                    }
                }

                clipper.End();
            }
        }

        WindowContentExtend.ExtendToLastItem(200);
        return result;
    }

    internal static UiResults DrawSymbolUiEntry(GraphUiContext context, SymbolUi symbolUi)
    {
        var result = UiResults.None;

        var symbolHash = symbolUi.Symbol.Id.GetHashCode();
        ImGui.PushID(symbolHash);

        var symbolNamespace = symbolUi.Symbol.Namespace;
        var isRelevantNamespace = IsRelevantNamespace(context, symbolNamespace);

        var type = symbolUi.Symbol.OutputDefinitions.Count > 0
                        ? symbolUi.Symbol.OutputDefinitions[0]?.ValueType
                        : null;

        
        TypeUiRegistry.TryGetPropertiesForType(type, out var properties);
        var color = properties.Color;


        ImGui.PushStyleColor(ImGuiCol.Header, ColorVariations.OperatorBackground.Apply(color).Rgba);
        var hoverColor = ColorVariations.OperatorBackgroundHover.Apply(color).Rgba;
        hoverColor.W = 0.3f;

        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, hoverColor);
        ImGui.PushStyleColor(ImGuiCol.Text, ColorVariations.OperatorLabel.Apply(color).Rgba);

        var isSelected = symbolUi == _selectedSymbolUi;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 14);
        ImGui.SetNextItemWidth(20);

        var size = ImGui.CalcTextSize(symbolUi.Symbol.Name);

        if (ImGui.Selectable($"##Selectable{symbolHash}",
                             isSelected,
                             ImGuiSelectableFlags.None,
                             new Vector2(size.X, 0)))
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

        ImGui.SameLine(ImGui.GetItemRectMin().X - ImGui.GetWindowPos().X);
        ImGui.TextUnformatted(symbolUi.Symbol.Name);

        if (type != null)
        {
            ImGui.SameLine(0,10);
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.3f);
            ImGui.TextUnformatted(type.Name.ToString());
            ImGui.PopStyleVar();
        }

        ImGui.PopStyleColor(3);
        ImGui.PopID();

        return result;
    }

    private static bool IsRelevantNamespace(GraphUiContext context, string symbolNamespace)
    {
        var projectNamespace = "user." + context.CompositionInstance.Symbol.SymbolPackage.AssemblyInformation.Name + ".";
        var compositionNameSpace = context.CompositionInstance.Symbol.Namespace;

        var isRelevantNamespace =
            symbolNamespace.StartsWith("Lib.")
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

    // Cached row height for clipper
    private static float _rowHeight = 0;

    [Flags]
    internal enum UiResults
    {
        None = 1 << 1,
        SelectionChanged = 1 << 2,
        FilterChanged = 1 << 3,
        Create = 1 << 4,
        Cancel = 1 << 5,
        ClickedOutside = 1 << 6,
    }
}
