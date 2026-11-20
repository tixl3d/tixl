#nullable enable
using System.Text;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.UiModel;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Flattens the shown help content into plain text for the Help window's copy button, so it can be pasted
/// into a translation service: just the title, the description and the parameter descriptions. The resource
/// footer (video references and links) is left out entirely — it is navigation, not translatable prose. The
/// markdown is converted with <see cref="MarkdownPlainText"/>, so the copy reads like the rendered doc.
/// </summary>
internal static class HelpTextExport
{
    /// <summary>The clipboard text for <paramref name="content"/>; empty when there is nothing to copy.</summary>
    internal static string Build(in HelpContent content)
    {
        var sb = new StringBuilder();

        if (content.SymbolUi is { } symbolUi)
        {
            sb.Append(symbolUi.Symbol.Name).Append('\n');
            sb.Append("in ").Append(symbolUi.Symbol.Namespace).Append('\n');

            if (!string.IsNullOrWhiteSpace(symbolUi.Description))
            {
                sb.Append('\n');
                MarkdownPlainText.Append(sb, symbolUi.Description);
            }

            AppendParameterDescriptions(sb, symbolUi);
        }
        else if (content.UiTopicTitle != null)
        {
            sb.Append(content.UiTopicTitle).Append('\n');

            if (!string.IsNullOrWhiteSpace(content.UiTopicDoc))
            {
                sb.Append('\n');
                MarkdownPlainText.Append(sb, content.UiTopicDoc);
            }
        }

        var text = sb.ToString().TrimEnd();
        return text.Length == 0 ? string.Empty : text + "\n";
    }

    /// <summary>Only the parameters with a description are shown in the window, so only those are copied.</summary>
    private static void AppendParameterDescriptions(StringBuilder sb, SymbolUi symbolUi)
    {
        var wroteHeader = false;
        foreach (var inputUi in symbolUi.InputUis.Values)
        {
            if (string.IsNullOrEmpty(inputUi.Description))
                continue;

            // Parameter descriptions keep every newline (the window renders them with hard line breaks).
            var description = MarkdownPlainText.ToText(inputUi.Description, hardLineBreaks: true).Trim();
            if (description.Length == 0)
                continue;

            var lines = description.Split('\n');

            if (!wroteHeader)
            {
                sb.Append('\n').Append("Parameter Details").Append('\n');
                wroteHeader = true;
            }

            // Continuation lines stay indented under their parameter, so a multi-paragraph description
            // doesn't read like a bunch of extra parameters.
            sb.Append("- ").Append(inputUi.InputDefinition.Name).Append(": ").Append(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                sb.Append('\n');
                if (lines[i].Length > 0)
                    sb.Append("  ").Append(lines[i]);
            }

            sb.Append('\n');
        }
    }
}
