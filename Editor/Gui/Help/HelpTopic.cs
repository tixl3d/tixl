#nullable enable
namespace T3.Editor.Gui.Help;

/// <summary>
/// Identifies a documentation topic shown in the <see cref="Windows.HelpWindow"/>: either an operator
/// (by symbol id) or a <c>ui:</c> UI topic (by its help-index key). Value-equality makes it usable as a
/// history entry.
/// </summary>
internal readonly record struct HelpTopic(Guid SymbolId, string? UiTopicKey)
{
    internal static HelpTopic ForOperator(Guid symbolId) => new(symbolId, null);

    /// <summary>Accepts both a full <c>ui:Id</c> key and a bare id; normalizes so both forms dedupe in history.</summary>
    internal static HelpTopic ForUiTopic(string keyOrId)
    {
        var key = keyOrId.StartsWith(UiPrefix, StringComparison.Ordinal) ? keyOrId : UiPrefix + keyOrId;
        return new HelpTopic(Guid.Empty, key);
    }

    internal bool IsEmpty => SymbolId == Guid.Empty && UiTopicKey == null;

    private const string UiPrefix = "ui:";
}

/// <summary>
/// Resolves a <c>ui:</c> topic to its display title and markdown doc. A bespoke
/// <c>.help/embedded/&lt;id&gt;.md</c> snippet wins over the topic registry's doc, so a documentation
/// icon can carry hand-written content while still falling back to the shared <c>ui:</c> topic body.
/// </summary>
internal static class UiTopicDocs
{
    /// <summary>
    /// True if the id names a known topic (registry entry or embedded snippet); <paramref name="markdown"/>
    /// may still be null for a registry topic whose doc hasn't been written yet.
    /// </summary>
    internal static bool TryGet(string keyOrId, out string title, out string? markdown)
    {
        var key = keyOrId.StartsWith("ui:", StringComparison.Ordinal) ? keyOrId : "ui:" + keyOrId;
        if (_cache.TryGetValue(key, out var cached))
        {
            title = cached.Title;
            markdown = cached.Doc;
            return cached.Found;
        }

        var id = key.Substring(3);
        var doc = EmbeddedHelpLoader.TryLoad(id);
        var resolvedTitle = id;
        var found = doc != null;

        if (HelpIndex.TryGetTopic(key, out var topic) && topic != null)
        {
            resolvedTitle = topic.Term;
            found = true;
            if (doc == null && !string.IsNullOrWhiteSpace(topic.Doc))
                doc = topic.Doc;
        }

        _cache[key] = (found, resolvedTitle, doc);
        title = resolvedTitle;
        markdown = doc;
        return found;
    }

    private static readonly Dictionary<string, (bool Found, string Title, string? Doc)> _cache = new();
}
