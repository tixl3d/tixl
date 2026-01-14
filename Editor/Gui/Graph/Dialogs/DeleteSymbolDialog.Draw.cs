#nullable enable
using ImGuiNET;
using T3.Core.Operator;
using T3.Editor.Gui.Styling;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Dialogs;

internal sealed partial class DeleteSymbolDialog
{
    /// <summary>
    /// Renders the main body of the delete confirmation dialog after symbol dependency analysis
    /// has completed successfully.
    /// </summary>
    /// <param name="symbol">The symbol currently selected for deletion.</param>
    /// <param name="info">Detailed dependency and classification information about the symbol
    /// obtained from <see cref="SymbolAnalysis.TryGetSymbolInfo"/>.</param>
    private static void DrawAnalysisUi(Symbol symbol, LocalSymbolInfo info)
    {
        var isProtected = TryGetRestriction(symbol, info, out var restriction);
        var isNamespaceMain = IsNamespaceMainSymbol(symbol);
        var symbolName = symbol.Name;

        _allowDeletion = !isProtected && !symbol.SymbolPackage.IsReadOnly;

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
        ImGui.TextWrapped(isProtected ? $"Can not delete [{symbolName}]" : $"Are you sure you want to delete [{symbolName}]?");
        ImGui.PopStyleColor();

        if (isProtected)
        {
            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextColored(UiColors.StatusAttention,
                              isNamespaceMain ? "This symbol is attached to the project namespace." : $"You can not delete symbols that are {restriction}");
            ImGui.PopFont();
        }

        if (isNamespaceMain)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
            ImGui.TextWrapped($"""
                              Symbol [{symbolName}] acts as the main symbol for namespace [{symbol.Namespace}]. 
                              Removing it directly can leave the project in a broken state. 
                              Use the namespace delete workflow (todo) instead of deleting this symbol.
                              """);
            ImGui.PopStyleColor();

            return;
        }

        if (!info.DependingSymbols.IsEmpty)
        {
            if (!_allowDeletion)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextWrapped(
                    $"Symbol [{symbolName}] is used by [{info.DependingSymbols.Count}] other projects/symbols, " +
                    "but deletion is blocked because it belongs to a protected library or read-only package.");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextWrapped($"[{symbolName}] is used in [{info.DependingSymbols.Count}] projects/symbols:");
                ImGui.PopStyleColor();

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ListSymbolNames(info.DependingSymbols);
                ImGui.PopStyleColor();

                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextWrapped("Clicking Force delete will automatically disconnect/clean all usages. " +
                                  "This may completely break these projects/symbols, and can *NOT* be undone.");
                ImGui.PopStyleColor();
            }
        }
        else
        {
            if (!_allowDeletion)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAttention.Rgba);
                ImGui.TextWrapped($"Symbol [{symbolName}] is not used by other symbols, " +
                                  $"but deletion is blocked because it belongs to a protected library or read-only package.");
                ImGui.PopStyleColor();
            }
            else
            {
                ImGui.PushStyleColor(ImGuiCol.Text, UiColors.Text.Rgba);
                ImGui.TextWrapped($"Symbol [{symbolName}] is not used by other symbols and can be safely deleted.");
                ImGui.PopStyleColor();
            }
        }
    }

    /// <summary>
    /// Renders a scrollable list of symbol names and their project namespaces for all
    /// symbols that reference the symbol being deleted.
    /// </summary>
    /// <param name="symbolIds">
    /// The set of symbol IDs that depend on the symbol currently considered for deletion.
    /// </param>
    private static void ListSymbolNames(IEnumerable<Guid> symbolIds)
    {
        if (_cachedMatches == null)
        {
            var allSymbolUis = EditorSymbolPackage.AllSymbolUis;
            var idSet = symbolIds.ToHashSet();
            _cachedMatches = allSymbolUis
                            .Where(s => idSet.Contains(s.Symbol.Id))
                            .OrderBy(s => s.Symbol.Namespace)
                            .ThenBy(s => s.Symbol.Name)
                            .ToList();
        }

        if (_cachedMatches.Count == 0)
            return;

        var fontSize = ImGui.GetFontSize();  // Current font size in pixels
        const int maxVisibleItems = 5;
        var itemHeight = fontSize + 4.0f;    // Font size + small padding
        var scrollHeight = itemHeight * maxVisibleItems;

        if (ImGui.BeginChild("SymbolList",
                new Vector2(0, scrollHeight),
                true))
        {
            var lastGroupName = string.Empty;
            foreach (var symbolUi in _cachedMatches)
            {
                var projectName = symbolUi.Symbol.SymbolPackage.RootNamespace;
                if (projectName != lastGroupName)
                {
                    lastGroupName = projectName;
                    var avail    = ImGui.GetContentRegionAvail();
                    var cursorPos = ImGui.GetCursorScreenPos();
                    var drawList  = ImGui.GetWindowDrawList();
                    var rectMax   = new Vector2(cursorPos.X + avail.X,
                                                cursorPos.Y + Fonts.FontSmall.FontSize + 4);
                    drawList.AddRectFilled(cursorPos, rectMax, UiColors.BackgroundFull.Fade(0.3f), 0.0f);

                    CustomComponents.StylizedText(projectName, Fonts.FontSmall, UiColors.Text);
                }

                var symbolLabel = "  " + symbolUi.Symbol.Name;
                CustomComponents.StylizedText(symbolLabel, Fonts.FontSmall, UiColors.Text);
            }
        }

        ImGui.EndChild();
    }
}
