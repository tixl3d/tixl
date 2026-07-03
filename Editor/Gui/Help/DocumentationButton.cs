#nullable enable
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.Gui.Windows;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Reusable help affordance for a window or panel header: a <see cref="Icon.Help"/> button. When a doc
/// exists for the id (a bespoke <c>.help/embedded/&lt;docId&gt;.md</c> snippet or the matching <c>ui:</c>
/// topic — see <see cref="UiTopicDocs"/>), clicking shows it in the Help window; hovering previews it
/// there (or falls back to a formatted tooltip while the window's hover mode is off). Without a doc the
/// button opens the canonical wiki page. Pass a plain id (e.g. <c>"RenderSettings"</c>) or a full
/// <c>"ui:RenderSettings"</c> key.
/// </summary>
internal static class DocumentationButton
{
    internal static void Draw(string docId, string wikiUrl, Vector2 size)
    {
        var hasDoc = UiTopicDocs.TryGet(docId, out _, out var markdown) && !string.IsNullOrWhiteSpace(markdown);

        if (CustomComponents.TransparentIconButton(Icon.Help, size))
        {
            if (hasDoc)
                HelpWindow.ShowTopic(HelpTopic.ForUiTopic(docId), showWindow: true);
            else
                CoreUi.Instance.OpenWithDefaultApplication(wikiUrl);
        }

        if (!ImGui.IsItemHovered())
            return;

        // While the Help window previews hovered topics, a tooltip would double the content.
        if (hasDoc && HelpWindow.HoverPreviewActive)
        {
            HoveredHelpTarget.SetTopic(docId);
            return;
        }

        var scale = T3Ui.UiScaleFactor;
        var tooltipWidth = TooltipWidth * scale;

        // Pin the width before BeginTooltip so the auto-resizing tooltip doesn't flash at viewport
        // width on its first frame.
        // Constrain width and give a generous-but-finite max height so the auto-resizing tooltip fits its
        // content without the spurious scrollbar a 0/FLT_MAX height range can leave on the first frame.
        var maxHeight = ImGui.GetMainViewport().WorkSize.Y - 40 * scale;
        ImGui.SetNextWindowSizeConstraints(new Vector2(tooltipWidth, 0), new Vector2(tooltipWidth, maxHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * scale);
        ImGui.BeginTooltip();

        if (hasDoc)
        {
            // Operator refs inside the tooltip suppress their own tooltip to avoid nesting.
            _markdown.Draw(markdown!,
                           onUrl: url => CoreUi.Instance.OpenWithDefaultApplication(url),
                           onOperatorRef: op => MarkdownOperatorLinks.HandleOperatorRef(op, suppressTooltip: true),
                           operatorColor: MarkdownOperatorLinks.GetOperatorColor);
            FormInputs.AddVerticalSpace(4);
        }

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(hasDoc
                                  ? "Click to read this in the Help window"
                                  : "Click to open the documentation in the wiki");
        ImGui.PopStyleColor();

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    private const float TooltipWidth = 360f;
    private static readonly MarkdownView _markdown = new(new MarkdownView.Options());
}
