#nullable enable
using ImGuiNET;
using T3.Core.SystemUi;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Reusable help affordance for a window or panel header: a <see cref="Icon.Help"/> button that
/// shows a doc as a formatted tooltip on hover and opens the canonical wiki page on click. Operator and
/// <c>[ui:Topic]</c> references in the doc become links. The doc text is a bespoke
/// <c>.help/embedded/&lt;docId&gt;.md</c> snippet when one exists, otherwise the matching <c>ui:</c> topic
/// from the help index — so an icon can reuse a shared topic doc instead of a dedicated embedded file.
/// Pass a plain id (e.g. <c>"RenderSettings"</c>) or a full <c>"ui:RenderSettings"</c> key for the fallback.
/// </summary>
internal static class DocumentationButton
{
    internal static void Draw(string docId, string wikiUrl, Vector2 size)
    {
        if (CustomComponents.TransparentIconButton(Icon.Help, size))
            CoreUi.Instance.OpenWithDefaultApplication(wikiUrl);

        if (!ImGui.IsItemHovered())
            return;

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

        var markdown = GetDoc(docId);
        if (markdown != null)
        {
            // Operator refs inside the tooltip suppress their own tooltip to avoid nesting.
            _markdown.Draw(markdown,
                           onUrl: url => CoreUi.Instance.OpenWithDefaultApplication(url),
                           onOperatorRef: op => MarkdownOperatorLinks.HandleOperatorRef(op, suppressTooltip: true),
                           operatorColor: MarkdownOperatorLinks.GetOperatorColor);
            FormInputs.AddVerticalSpace(4);
        }

        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted("Click to open the documentation in the wiki");
        ImGui.PopStyleColor();

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    private static string? GetDoc(string docId)
    {
        if (_cache.TryGetValue(docId, out var cached))
            return cached;

        // A bespoke embedded snippet wins; otherwise fall back to the ui: topic registry so an icon can
        // reuse a shared topic doc. A "ui:"-prefixed docId is registry-only (':' is not a valid filename).
        var loaded = docId.Contains(':') ? null : EmbeddedHelpLoader.TryLoad(docId);
        if (loaded == null)
        {
            var topicKey = docId.StartsWith("ui:", StringComparison.Ordinal) ? docId : "ui:" + docId;
            if (HelpIndex.TryGetTopic(topicKey, out var topic) && topic != null && !string.IsNullOrWhiteSpace(topic.Doc))
                loaded = topic.Doc;
        }

        _cache[docId] = loaded;
        return loaded;
    }

    private const float TooltipWidth = 360f;
    private static readonly Dictionary<string, string?> _cache = new();
    private static readonly MarkdownView _markdown = new(new MarkdownView.Options());
}
