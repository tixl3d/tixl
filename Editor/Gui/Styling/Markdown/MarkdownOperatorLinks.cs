#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator;
using T3.Editor.Gui.Help;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel;
using T3.Editor.UiModel.InputsAndTypes;

namespace T3.Editor.Gui.Styling.Markdown;

/// <summary>
/// Supplies <see cref="MarkdownView.OperatorRefRendered"/>: resolves a bare <c>[OpName]</c> markdown
/// reference to a symbol, shows a hover tooltip (namespace + description), and on click reveals the
/// operator in the Symbol Library. Unresolved names get no interaction (the renderer already shows
/// them as plain text via the missing UrlIndex).
/// </summary>
internal static class MarkdownOperatorLinks
{
    /// <summary>
    /// Hover/click handler for <c>[OpName]</c> fragments — pass as <c>MarkdownView</c>'s <c>onOperatorRef</c>.
    /// Set <paramref name="suppressTooltip"/> when the markdown is itself drawn inside a tooltip, since
    /// ImGui can't nest tooltips.
    /// </summary>
    internal static void HandleOperatorRef(string opName, bool suppressTooltip = false)
    {
        if (opName.StartsWith("ui:", StringComparison.Ordinal))
        {
            HandleTopicRef(opName, suppressTooltip);
            return;
        }

        if (!ImGui.IsItemHovered())
            return;

        if (!TryResolve(opName, out var symbol))
            return;

        // suppressTooltip avoids nesting when the markdown is already inside a tooltip (e.g. the
        // operator's own description, which can itself contain [OpName] references).
        if (!suppressTooltip)
        {
            var scale = T3Ui.UiScaleFactor;
            var tooltipWidth = TooltipWidth * scale;

            // Pin the width before BeginTooltip so the auto-resizing tooltip doesn't flash at viewport
            // width on its first frame.
            ImGui.SetNextWindowSizeConstraints(new Vector2(tooltipWidth, 0), new Vector2(tooltipWidth, float.MaxValue));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * scale);
            ImGui.BeginTooltip();

            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextUnformatted(symbol.Name);
            ImGui.PopFont();
            ImGui.TextColored(UiColors.TextMuted, symbol.Namespace);

            if (symbol.TryGetSymbolUi(out var symbolUi) && !string.IsNullOrWhiteSpace(symbolUi.Description))
            {
                ImGui.Spacing();
                // Render the description as markdown too — code spans, lists, and [OpName] refs read
                // correctly. Operator refs inside the tooltip suppress their own tooltip to avoid nesting.
                _tooltipMarkdown.Draw(symbolUi.Description,
                                      onOperatorRef: op => HandleOperatorRef(op, suppressTooltip: true),
                                      operatorColor: GetOperatorColor);
            }

            ImGui.EndTooltip();
            ImGui.PopStyleVar();
        }

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            WindowManager.SymbolLibrary.Reveal(symbol.Id);
    }

    /// <summary>
    /// Hover tooltip for a <c>[ui:Id]</c> fragment — the help index's UI topic (editor concept/component).
    /// Shows the topic term and its doc; an unknown id gets no interaction.
    /// </summary>
    private static void HandleTopicRef(string topicKey, bool suppressTooltip)
    {
        if (!ImGui.IsItemHovered())
            return;

        if (!HelpIndex.TryGetTopic(topicKey, out var topic) || topic == null)
            return;

        if (suppressTooltip)
            return;

        var scale = T3Ui.UiScaleFactor;
        var tooltipWidth = TooltipWidth * scale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(tooltipWidth, 0), new Vector2(tooltipWidth, float.MaxValue));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * scale);
        ImGui.BeginTooltip();

        ImGui.PushFont(Fonts.FontBold);
        ImGui.TextUnformatted(topic.Term);
        ImGui.PopFont();
        ImGui.TextColored(UiColors.TextMuted, "UI topic");

        if (!string.IsNullOrWhiteSpace(topic.Doc))
        {
            ImGui.Spacing();
            _tooltipMarkdown.Draw(topic.Doc,
                                  onOperatorRef: op => HandleOperatorRef(op, suppressTooltip: true),
                                  operatorColor: GetOperatorColor);
        }

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// Text color for an <c>[OpName]</c> / <c>[ui:Id]</c> fragment — the operator's output-type color, a fixed
    /// accent for UI topics, or the plain body color for an unknown name. Pass as <c>operatorColor</c>.
    /// </summary>
    internal static Color GetOperatorColor(string opName)
    {
        if (opName.StartsWith("ui:", StringComparison.Ordinal))
            return HelpIndex.TryGetTopic(opName, out _) ? UiColors.StatusAutomated : UiColors.Text;

        if (!TryResolve(opName, out var symbol))
            return UiColors.Text;

        return symbol.OutputDefinitions.Count > 0
                   ? TypeUiRegistry.GetPropertiesForType(symbol.OutputDefinitions[0]?.ValueType).Color
                   : UiColors.Gray;
    }

    private static bool TryResolve(string opName, out Symbol symbol)
    {
        // Rebuilt when the symbol structure changes so references to ops from
        // later-loaded projects resolve too.
        var structureVersion = EditorSymbolPackage.SymbolStructureVersionCounter;
        if (_symbolsByName == null || structureVersion != _lookupVersion)
        {
            _symbolsByName = BuildLookup();
            _lookupVersion = structureVersion;
        }

        return _symbolsByName.TryGetValue(opName, out symbol!);
    }

    private static Dictionary<string, Symbol> BuildLookup()
    {
        var map = new Dictionary<string, Symbol>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in EditorSymbolPackage.AllSymbols)
            map[symbol.Name] = symbol; // duplicate names: last wins — fine for documentation links
        return map;
    }

    private static Dictionary<string, Symbol>? _symbolsByName;
    private static int _lookupVersion = -1;

    private const float TooltipWidth = 320f;
    private static readonly MarkdownView _tooltipMarkdown = new(new MarkdownView.Options());
}
