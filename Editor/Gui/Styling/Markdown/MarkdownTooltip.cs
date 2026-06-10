#nullable enable
using ImGuiNET;

namespace T3.Editor.Gui.Styling.Markdown;

/// <summary>
/// Renders description markdown inside an ImGui tooltip. A dummy pins the wrap width because
/// auto-sizing tooltips report a useless ContentRegionAvail on their first frame. Op-refs
/// suppress their own tooltip (no nesting). One shared view is enough — only a single tooltip
/// is visible at a time.
/// </summary>
internal static class MarkdownTooltip
{
    public static void Draw(string markdown)
    {
        ImGui.Dummy(new Vector2(WidthPx * T3Ui.UiScaleFactor, 1));
        _view.Draw(markdown,
                   onOperatorRef: _suppressedOpRefHandler,
                   operatorColor: MarkdownOperatorLinks.GetOperatorColor);
    }

    private static readonly MarkdownView.OperatorRefRendered _suppressedOpRefHandler =
        static opName => MarkdownOperatorLinks.HandleOperatorRef(opName, suppressTooltip: true);

    // Descriptions are mostly authored as plain text, so newlines stay hard line breaks.
    private static readonly MarkdownView _view = new(new MarkdownView.Options { HardLineBreaks = true });

    private const float WidthPx = 320f;
}
