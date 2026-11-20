#nullable enable
using System.Text;

namespace T3.Editor.Gui.Styling.Markdown;

/// <summary>
/// Flattens the same markdown subset the views render into plain text — no layout, no ImGui. Heading lines,
/// list markers and blank-line separation are kept so the result still reads like the rendered doc, while
/// emphasis/code markers are dropped. Links and operator references keep the brackets they were written
/// with but not their target, since the copy is read in a translator, where a URL only adds noise.
/// </summary>
internal static class MarkdownPlainText
{
    internal static string ToText(string markdown, bool hardLineBreaks = false)
    {
        var sb = new StringBuilder(markdown.Length);
        Append(sb, markdown, hardLineBreaks);
        return sb.ToString();
    }

    internal static void Append(StringBuilder sb, string markdown, bool hardLineBreaks = false)
    {
        var doc = new ParsedDoc();
        MarkdownParser.Parse(markdown, doc, hardLineBreaks);

        var wroteAny = false;
        var sawBlank = false;
        var previousWasHeading = false;

        foreach (var line in doc.Lines)
        {
            if (line.Kind == LineKind.Blank)
            {
                sawBlank = true;
                continue;
            }

            var isHeading = line.Kind is LineKind.H1 or LineKind.H2 or LineKind.H3;

            // A heading gets a blank line after it even when the source keeps the next line right below.
            if (wroteAny && (sawBlank || (previousWasHeading && !isHeading)))
                sb.Append('\n');

            switch (line.Kind)
            {
                case LineKind.Bullet:
                    sb.Append(' ', line.IndentLevel * 2).Append("- ");
                    break;
                case LineKind.Numbered:
                    sb.Append(' ', line.IndentLevel * 2).Append(line.Number).Append(". ");
                    break;
            }

            AppendRuns(sb, doc, line);
            sb.Append('\n');

            wroteAny = true;
            sawBlank = false;
            previousWasHeading = isHeading;
        }
    }

    private static void AppendRuns(StringBuilder sb, ParsedDoc doc, in Line line)
    {
        for (var runIndex = 0; runIndex < line.RunCount; runIndex++)
        {
            var run = doc.Runs[line.RunStart + runIndex];
            var text = doc.Source.Substring(run.Text.Start, run.Text.Length);

            // "[Blur]" and "[the graph window]" stay recognizable as references; "[docs](url)" loses the
            // target but keeps its brackets, so nothing that was bracketed in the source turns into plain prose.
            if ((run.Style & (RunStyle.Link | RunStyle.OpRef)) != 0)
            {
                sb.Append('[').Append(text).Append(']');
                continue;
            }

            sb.Append(text);
        }
    }
}
