#nullable enable
using T3.Editor.Gui.Windows;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Help;

/// <summary>
/// What the <see cref="T3.Editor.Gui.Windows.HelpWindow"/> shows for one <see cref="HelpTopic"/>: the
/// operator or <c>ui:</c> topic doc, plus the key and hand-authored links that feed the resource footer.
/// Resolved once per frame so the doc body, the header icons and the clipboard export agree on what is shown.
/// </summary>
internal readonly record struct HelpContent(
    SymbolUi? SymbolUi,
    string? UiTopicTitle,
    string? UiTopicDoc,
    string? ResourceKey,
    IReadOnlyList<VideoResourceList.LinkRow> Links)
{
    /// <summary>False for the empty state and for topics whose operator or doc is no longer available.</summary>
    internal bool HasContent => SymbolUi != null || UiTopicTitle != null;

    internal static HelpContent For(in HelpTopic topic)
    {
        if (topic.UiTopicKey != null)
        {
            return UiTopicDocs.TryGet(topic.UiTopicKey, out var title, out var doc)
                       ? new HelpContent(null, title, doc, topic.UiTopicKey, [])
                       : Empty;
        }

        if (topic.SymbolId == Guid.Empty || !SymbolUiRegistry.TryGetSymbolUi(topic.SymbolId, out var symbolUi))
            return Empty;

        // The hand-authored symbol links rank into the same footer list as the extracted video references.
        var mentionKey = "op:" + symbolUi.Symbol.Namespace + "." + symbolUi.Symbol.Name;
        return new HelpContent(symbolUi,
                               null,
                               null,
                               mentionKey,
                               OperatorHelp.DocumentationRenderer.GetLinkRows(symbolUi));
    }

    internal static readonly HelpContent Empty = new(null, null, null, null, []);
}
