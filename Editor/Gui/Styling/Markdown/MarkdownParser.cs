#nullable enable

namespace T3.Editor.Gui.Styling.Markdown;

/// <summary>
/// Parses a small fixed markdown subset into a logical-line + inline-run representation.
/// Runs reference the owning source string by (start, length) slices — no substring allocation.
/// </summary>
internal static class MarkdownParser
{
    public static void Parse(string source, ParsedDoc doc, bool hardLineBreaks = false)
    {
        // Soft line breaks: in markdown a single newline between two
        // paragraph-shaped lines means "join with a space", not "force line
        // break". CommonMark-style. Special lines (headings, list items,
        // blanks) keep their hard breaks. Callers that want every `\n` to be
        // a hard break (e.g. legacy TourPoint content) can pass true.
        var normalized = hardLineBreaks
                             ? source.Replace("\r\n", "\n")
                             : NormalizeSoftLineBreaks(source);
        doc.Reset(normalized);

        var i = 0;
        var len = normalized.Length;
        while (i < len)
        {
            // Find end of logical line (terminator excluded).
            var lineStart = i;
            while (i < len && normalized[i] != '\n')
                i++;
            var lineEnd = i;
            // Normalisation already collapsed \r\n → \n, so no CR cleanup needed.

            ParseLine(normalized, lineStart, lineEnd, doc);

            // Skip the \n.
            if (i < len && normalized[i] == '\n')
                i++;
        }
    }

    private static string NormalizeSoftLineBreaks(string source)
    {
        var raw = source.Replace("\r\n", "\n");
        var lines = raw.Split('\n');
        var sb = new System.Text.StringBuilder(raw.Length);
        var prevKind = SoftLineKind.None;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var kind = ClassifySoftLine(line, out var hasLeadingWhitespace);

            // Two folding cases:
            //   1. Paragraph after Paragraph — classic CommonMark soft line break.
            //   2. Indented Paragraph after a list item — "lazy continuation"
            //      so the item's content keeps reading as one wrapped run.
            // Anything else (blank, heading, new list item) keeps the hard \n.
            var foldIntoListItem = kind == SoftLineKind.Paragraph
                                    && prevKind == SoftLineKind.ListItem
                                    && hasLeadingWhitespace;
            var foldIntoParagraph = kind == SoftLineKind.Paragraph
                                    && prevKind == SoftLineKind.Paragraph;
            var fold = foldIntoListItem || foldIntoParagraph;

            if (sb.Length > 0)
                sb.Append(fold ? ' ' : '\n');

            // Trim leading whitespace from a folded line so the join doesn't
            // produce "foo   bar".
            sb.Append(fold ? line.TrimStart() : line);

            // When we fold a continuation into a list item, keep prevKind as
            // ListItem so subsequent indented lines also fold instead of
            // detaching after the first.
            if (!foldIntoListItem)
                prevKind = kind;
        }

        return sb.ToString();
    }

    private static SoftLineKind ClassifySoftLine(string line, out bool hasLeadingWhitespace)
    {
        hasLeadingWhitespace = line.Length > 0 && (line[0] == ' ' || line[0] == '\t');
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
            return SoftLineKind.Blank;
        if (trimmed[0] == '#')
            return SoftLineKind.Heading;
        if ((trimmed[0] == '-' || trimmed[0] == '*') && trimmed.Length > 1 && trimmed[1] == ' ')
            return SoftLineKind.ListItem;
        if (char.IsDigit(trimmed[0]))
        {
            var i = 0;
            while (i < trimmed.Length && char.IsDigit(trimmed[i]))
                i++;
            if (i + 1 < trimmed.Length && trimmed[i] == '.' && trimmed[i + 1] == ' ')
                return SoftLineKind.ListItem;
        }
        return SoftLineKind.Paragraph;
    }

    private enum SoftLineKind
    {
        None,
        Blank,
        Paragraph,
        Heading,
        ListItem,
    }

    private static void ParseLine(string src, int from, int to, ParsedDoc doc)
    {
        // Indent: count leading spaces (tab = 2 spaces). Depth = indent / 2.
        var indentSpaces = 0;
        var p = from;
        while (p < to)
        {
            var c = src[p];
            if (c == ' ')
                indentSpaces++;
            else if (c == '\t')
                indentSpaces += 2;
            else
                break;
            p++;
        }

        // Blank line.
        if (p == to)
        {
            doc.Lines.Add(new Line
                              {
                                  Kind = LineKind.Blank,
                                  IndentLevel = 0,
                                  RunStart = doc.Runs.Count,
                                  RunCount = 0,
                              });
            return;
        }

        var depth = (byte)(indentSpaces / 2);

        // Heading.
        var hashCount = 0;
        var hp = p;
        while (hp < to && src[hp] == '#' && hashCount < 3)
        {
            hashCount++;
            hp++;
        }

        if (hashCount > 0 && hp < to && src[hp] == ' ')
        {
            var contentStart = hp + 1;
            var kind = hashCount switch
                          {
                              1 => LineKind.H1,
                              2 => LineKind.H2,
                              _ => LineKind.H3,
                          };
            var runStart = doc.Runs.Count;
            ParseInline(src, contentStart, to, doc);
            doc.Lines.Add(new Line
                              {
                                  Kind = kind,
                                  IndentLevel = 0,
                                  RunStart = runStart,
                                  RunCount = doc.Runs.Count - runStart,
                              });
            return;
        }

        // Bullet list (- or *).
        if ((src[p] == '-' || src[p] == '*') && p + 1 < to && src[p + 1] == ' ')
        {
            var contentStart = p + 2;
            var runStart = doc.Runs.Count;
            ParseInline(src, contentStart, to, doc);
            doc.Lines.Add(new Line
                              {
                                  Kind = LineKind.Bullet,
                                  IndentLevel = depth,
                                  RunStart = runStart,
                                  RunCount = doc.Runs.Count - runStart,
                              });
            return;
        }

        // Numbered list (digits followed by ". ").
        if (char.IsDigit(src[p]))
        {
            var np = p;
            var num = 0;
            while (np < to && char.IsDigit(src[np]))
            {
                num = num * 10 + (src[np] - '0');
                np++;
            }

            if (np < to - 1 && src[np] == '.' && src[np + 1] == ' ')
            {
                var contentStart = np + 2;
                var runStart = doc.Runs.Count;
                ParseInline(src, contentStart, to, doc);
                doc.Lines.Add(new Line
                                  {
                                      Kind = LineKind.Numbered,
                                      IndentLevel = depth,
                                      Number = num,
                                      RunStart = runStart,
                                      RunCount = doc.Runs.Count - runStart,
                                  });
                return;
            }
        }

        // Plain paragraph line. Indent only meaningful for lists; we discard it
        // here (lazy continuation is out of scope per the plan).
        {
            var runStart = doc.Runs.Count;
            ParseInline(src, p, to, doc);
            doc.Lines.Add(new Line
                              {
                                  Kind = LineKind.Paragraph,
                                  IndentLevel = 0,
                                  RunStart = runStart,
                                  RunCount = doc.Runs.Count - runStart,
                              });
        }
    }

    private static void ParseInline(string src, int from, int to, ParsedDoc doc)
    {
        var i = from;
        var plainStart = from;

        while (i < to)
        {
            var c = src[i];

            // **bold**
            if (c == '*' && i + 1 < to && src[i + 1] == '*')
            {
                var close = IndexOfDouble(src, i + 2, to, '*');
                if (close >= 0)
                {
                    FlushPlain(src, plainStart, i, doc);
                    doc.Runs.Add(new Run(new Slice(i + 2, close - (i + 2)), RunStyle.Bold, -1));
                    i = close + 2;
                    plainStart = i;
                    continue;
                }
            }

            // `code`
            if (c == '`')
            {
                var close = IndexOf(src, i + 1, to, '`');
                if (close >= 0)
                {
                    FlushPlain(src, plainStart, i, doc);
                    doc.Runs.Add(new Run(new Slice(i + 1, close - (i + 1)), RunStyle.Code, -1));
                    i = close + 1;
                    plainStart = i;
                    continue;
                }
            }

            // [label](url) or [OpName]
            if (c == '[')
            {
                var close = IndexOf(src, i + 1, to, ']');
                if (close >= 0)
                {
                    var labelStart = i + 1;
                    var labelLen = close - labelStart;

                    // Link: [label](url)
                    if (close + 1 < to && src[close + 1] == '(')
                    {
                        var urlClose = IndexOf(src, close + 2, to, ')');
                        if (urlClose >= 0)
                        {
                            FlushPlain(src, plainStart, i, doc);
                            var urlSlice = new Slice(close + 2, urlClose - (close + 2));
                            var urlIndex = doc.Urls.Count;
                            doc.Urls.Add(urlSlice);
                            doc.Runs.Add(new Run(new Slice(labelStart, labelLen), RunStyle.Link, urlIndex));
                            i = urlClose + 1;
                            plainStart = i;
                            continue;
                        }
                    }

                    // Operator reference: [Identifier]
                    // UI-topic reference:  [ui:Identifier]  or with an explicit label  [ui:Identifier|display text]
                    if (labelLen > 0)
                    {
                        // A UI-topic ref may carry a "|display text" label so prose can read naturally
                        // ("the [ui:ParameterWindow|Parameter window]") while still linking the ui: key.
                        var pipe = IndexOf(src, labelStart, close, '|');
                        var keyLen = (pipe >= 0 ? pipe : close) - labelStart;

                        var isUiTopic = IsUiTopicRef(src, labelStart, keyLen);
                        var isOpRef = pipe < 0 && IsIdentifier(src, labelStart, labelLen);

                        if (isUiTopic || isOpRef)
                        {
                            FlushPlain(src, plainStart, i, doc);

                            // The key the handler resolves ("ui:Graph" or the operator name).
                            var keySlice = new Slice(labelStart, keyLen);
                            var urlIndex = doc.Urls.Count;
                            doc.Urls.Add(keySlice);

                            // Displayed text: the explicit "|label", else the key with the "ui:" prefix dropped.
                            Slice textSlice;
                            if (pipe >= 0)
                                textSlice = new Slice(pipe + 1, close - (pipe + 1));
                            else if (isUiTopic)
                                textSlice = new Slice(labelStart + 3, keyLen - 3); // drop the "ui:" prefix
                            else
                                textSlice = keySlice;

                            doc.Runs.Add(new Run(textSlice, RunStyle.OpRef, urlIndex));
                            i = close + 1;
                            plainStart = i;
                            continue;
                        }
                    }
                }
            }

            i++;
        }

        FlushPlain(src, plainStart, to, doc);
    }

    private static void FlushPlain(string src, int from, int to, ParsedDoc doc)
    {
        if (to > from)
            doc.Runs.Add(new Run(new Slice(from, to - from), RunStyle.None, -1));
    }

    private static int IndexOf(string src, int from, int to, char target)
    {
        for (var i = from; i < to; i++)
        {
            if (src[i] == target)
                return i;
        }

        return -1;
    }

    private static int IndexOfDouble(string src, int from, int to, char target)
    {
        for (var i = from; i < to - 1; i++)
        {
            if (src[i] == target && src[i + 1] == target)
                return i;
        }

        return -1;
    }

    private static bool IsIdentifier(string src, int from, int length)
    {
        for (var i = 0; i < length; i++)
        {
            var c = src[from + i];
            var ok = (c >= 'A' && c <= 'Z')
                     || (c >= 'a' && c <= 'z')
                     || (c >= '0' && c <= '9')
                     || c == '_';
            if (!ok)
                return false;
        }

        return true;
    }

    /// <summary>A help-index UI-topic link: <c>[ui:Identifier]</c> (the <c>ui:</c> prefix then a plain identifier).</summary>
    private static bool IsUiTopicRef(string src, int from, int length)
    {
        return length > 3 && src[from] == 'u' && src[from + 1] == 'i' && src[from + 2] == ':'
               && IsIdentifier(src, from + 3, length - 3);
    }
}
