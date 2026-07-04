#nullable enable
using ImGuiNET;

namespace T3.Editor.Gui.Styling.Markdown;

/// <summary>
/// Layout pass: wraps the parser's runs into visual line boxes and computes
/// accumulated Y. Must be called inside an ImGui frame because it uses
/// CalcTextSize for measuring with the active font.
/// </summary>
internal static class MarkdownLayout
{
    public static void Build(ParsedDoc doc, LayoutResult result, in MarkdownView.Options opt, float wrapWidthPx)
    {
        result.Reset();

        // Materialise URL strings once.
        for (var i = 0; i < doc.Urls.Count; i++)
        {
            var s = doc.Urls[i];
            result.Urls.Add(doc.Source.Substring(s.Start, s.Length));
        }

        var scale = T3Ui.UiScaleFactor;
        var indentPx = opt.IndentPx > 0 ? opt.IndentPx * scale : 14f * scale;
        var markerPx = 16f * scale;

        var bodyFont = ResolveBodyFont(opt);
        var opRefFont = ResolveOpRefFont(opt);
        var bodyHeight = MeasureLineHeight(bodyFont);
        var h1Height = MeasureLineHeight(Fonts.FontLarge);
        var h2Height = MeasureLineHeight(Fonts.FontBold);
        var h3Height = h2Height;

        var y = 0f;
        var prevExisted = false;
        var prevWasHeading = false;
        var prevWasListItem = false;
        var sawBlank = false;

        foreach (var line in doc.Lines)
        {
            if (line.Kind == LineKind.Blank)
            {
                sawBlank = true;
                continue;
            }

            // Spacing above this line.
            var isListItem = line.Kind is LineKind.Bullet or LineKind.Numbered;
            var spacing = 0f;
            if (prevExisted)
            {
                var isHeading = line.Kind is LineKind.H1 or LineKind.H2 or LineKind.H3;
                if (isHeading)
                {
                    if (prevWasHeading)
                        spacing = 0;
                    else if (line.Kind == LineKind.H1)
                        spacing = 12 * scale;
                    else
                        spacing = 8 * scale;
                }
                else
                {
                    if (prevWasHeading)
                        spacing = 2 * scale;
                    else
                        spacing = 3 * scale;
                }

                // Extra gap between consecutive list items for readability.
                if (isListItem && prevWasListItem)
                    spacing += 2 * scale;

                if (sawBlank)
                    spacing += 4 * scale;
            }

            y += spacing;

            // Pick the font for this line so wrapping measures correctly.
            var lineFont = line.Kind switch
                              {
                                  LineKind.H1 => Fonts.FontLarge,
                                  LineKind.H2 or LineKind.H3 => Fonts.FontBold,
                                  _ => bodyFont,
                              };

            var lineHeight = line.Kind switch
                                {
                                    LineKind.H1 => h1Height,
                                    LineKind.H2 => h2Height,
                                    LineKind.H3 => h3Height,
                                    _ => bodyHeight,
                                };

            // Left offset: list items get depth indent + marker column.
            float contentLeft;
            if (line.Kind is LineKind.Bullet or LineKind.Numbered)
                contentLeft = line.IndentLevel * indentPx + markerPx;
            else
                contentLeft = 0;

            var effectiveWrap = wrapWidthPx > contentLeft + 16f
                                    ? wrapWidthPx
                                    : contentLeft + 200f;

            EmitLineRuns(doc, result, line, lineFont, opRefFont, lineHeight,
                         contentLeft, effectiveWrap, ref y, scale);

            prevExisted = true;
            prevWasHeading = line.Kind is LineKind.H1 or LineKind.H2 or LineKind.H3;
            prevWasListItem = isListItem;
            sawBlank = false;
        }

        result.TotalHeight = y;
    }

    private static void EmitLineRuns(ParsedDoc doc, LayoutResult result, Line line,
                                     ImFontPtr lineFont, ImFontPtr opRefFont, float lineHeight,
                                     float contentLeft, float wrapWidth,
                                     ref float y, float scale)
    {
        var src = doc.Source;
        var penX = contentLeft;
        var firstVisualLineOfLogical = true;
        var fragmentStart = result.Fragments.Count;

        ImGui.PushFont(lineFont);
        try
        {
            for (var ri = 0; ri < line.RunCount; ri++)
            {
                var run = doc.Runs[line.RunStart + ri];
                EmitRun(src, run, opRefFont, scale, ref penX, contentLeft, wrapWidth,
                        result, line, lineHeight, ref y,
                        ref firstVisualLineOfLogical, ref fragmentStart);
            }
        }
        finally
        {
            ImGui.PopFont();
        }

        // Close out the final visual line.
        FlushVisualLine(result, line, lineHeight, ref y,
                        ref firstVisualLineOfLogical, ref fragmentStart);
    }

    private static void EmitRun(string src, Run run, ImFontPtr opRefFont, float scale, ref float penX,
                                float contentLeft, float wrapWidth,
                                LayoutResult result, Line line, float lineHeight,
                                ref float y, ref bool firstVisualLineOfLogical,
                                ref int fragmentStart)
    {
        // Bold/Code/OpRef runs need their own font for accurate measurement.
        var pushed = false;
        var isOpRef = (run.Style & RunStyle.OpRef) != 0;
        if (isOpRef)
        {
            // Reserve the horizontal padding of the box the renderer draws behind op-refs.
            penX += MarkdownView.OpRefPaddingX * scale;
            ImGui.PushFont(opRefFont);
            pushed = true;
        }
        else if ((run.Style & RunStyle.Bold) != 0 || (run.Style & RunStyle.Link) != 0)
        {
            // Links inherit normal font (color/style differs); bold inside heading would
            // also already be bold via the line font. Push bold for explicit bold runs.
            if ((run.Style & RunStyle.Bold) != 0)
            {
                ImGui.PushFont(Fonts.FontBold);
                pushed = true;
            }
        }
        else if ((run.Style & RunStyle.Code) != 0)
        {
            ImGui.PushFont(Fonts.Code);
            pushed = true;
        }

        try
        {
            var sliceStart = run.Text.Start;
            var sliceLen = run.Text.Length;
            EmitSlice(src, sliceStart, sliceLen, run.Style, run.UrlIndex,
                      ref penX, contentLeft, wrapWidth,
                      result, line, lineHeight, ref y,
                      ref firstVisualLineOfLogical, ref fragmentStart);
        }
        finally
        {
            if (pushed)
                ImGui.PopFont();
        }

        if (isOpRef)
            penX += MarkdownView.OpRefPaddingX * scale;
    }

    private static void EmitSlice(string src, int sliceStart, int sliceLen,
                                  RunStyle style, int urlIndex,
                                  ref float penX, float contentLeft, float wrapWidth,
                                  LayoutResult result, Line line, float lineHeight,
                                  ref float y, ref bool firstVisualLineOfLogical,
                                  ref int fragmentStart)
    {
        if (sliceLen <= 0)
            return;

        // At line start, drop any leading whitespace from this slice. Source
        // text often has " " between runs (e.g. "type `RG` (the …)"); when the
        // wrap puts that boundary at the start of a visual line, the leading
        // space is visually wrong.
        if (Math.Abs(penX - contentLeft) < 0.5f)
        {
            while (sliceLen > 0 && src[sliceStart] == ' ')
            {
                sliceStart++;
                sliceLen--;
            }
            if (sliceLen <= 0)
                return;
        }

        var text = src.Substring(sliceStart, sliceLen);
        var width = ImGui.CalcTextSize(text).X;

        // Case 1: fits on current visual line.
        if (penX + width <= wrapWidth)
        {
            AddFragment(result, text, style, urlIndex);
            penX += width;
            return;
        }

        // Case 2: try to break at a space such that the prefix fits on this line.
        var available = wrapWidth - penX;
        var spaceIdx = FindLastFittingSpace(src, sliceStart, sliceLen, available);
        if (spaceIdx > 0)
        {
            EmitSlice(src, sliceStart, spaceIdx, style, urlIndex,
                      ref penX, contentLeft, wrapWidth,
                      result, line, lineHeight, ref y,
                      ref firstVisualLineOfLogical, ref fragmentStart);
            BreakLine(result, line, lineHeight, ref y, ref penX, contentLeft,
                      ref firstVisualLineOfLogical, ref fragmentStart);
            EmitSlice(src, sliceStart + spaceIdx + 1, sliceLen - spaceIdx - 1,
                      style, urlIndex, ref penX, contentLeft, wrapWidth,
                      result, line, lineHeight, ref y,
                      ref firstVisualLineOfLogical, ref fragmentStart);
            return;
        }

        // Case 3: no fitting space on the current line. If we have content
        // already, break and retry on a fresh line — the next pass will have
        // more room and can find a space.
        if (penX > contentLeft + 0.5f)
        {
            BreakLine(result, line, lineHeight, ref y, ref penX, contentLeft,
                      ref firstVisualLineOfLogical, ref fragmentStart);
            EmitSlice(src, sliceStart, sliceLen, style, urlIndex,
                      ref penX, contentLeft, wrapWidth,
                      result, line, lineHeight, ref y,
                      ref firstVisualLineOfLogical, ref fragmentStart);
            return;
        }

        // Case 4: we're on a fresh line and no space-prefix fits. Prefer breaking
        // at the FIRST space (preserves word boundaries) over a char-split — the
        // line may overflow visually, but readability beats cosmetics.
        var firstSpace = IndexOfChar(src, sliceStart, sliceLen, ' ');
        if (firstSpace > 0)
        {
            EmitOversizeFragment(src, sliceStart, firstSpace, style, urlIndex, result, ref penX);
            BreakLine(result, line, lineHeight, ref y, ref penX, contentLeft,
                      ref firstVisualLineOfLogical, ref fragmentStart);
            EmitSlice(src, sliceStart + firstSpace + 1, sliceLen - firstSpace - 1,
                      style, urlIndex, ref penX, contentLeft, wrapWidth,
                      result, line, lineHeight, ref y,
                      ref firstVisualLineOfLogical, ref fragmentStart);
            return;
        }

        // Case 5: a single token that exceeds the line width. Force-break by
        // character as a last resort.
        var splitAt = FindForcedBreak(src, sliceStart, sliceLen, wrapWidth - contentLeft);
        if (splitAt <= 0)
            splitAt = Math.Max(1, sliceLen / 2);
        EmitOversizeFragment(src, sliceStart, splitAt, style, urlIndex, result, ref penX);
        BreakLine(result, line, lineHeight, ref y, ref penX, contentLeft,
                  ref firstVisualLineOfLogical, ref fragmentStart);
        EmitSlice(src, sliceStart + splitAt, sliceLen - splitAt, style, urlIndex,
                  ref penX, contentLeft, wrapWidth,
                  result, line, lineHeight, ref y,
                  ref firstVisualLineOfLogical, ref fragmentStart);
    }

    private static void EmitOversizeFragment(string src, int from, int len,
                                             RunStyle style, int urlIndex,
                                             LayoutResult result, ref float penX)
    {
        if (len <= 0)
            return;
        var text = src.Substring(from, len);
        AddFragment(result, text, style, urlIndex);
        penX += ImGui.CalcTextSize(text).X;
    }

    private static int IndexOfChar(string src, int from, int len, char target)
    {
        for (var i = 0; i < len; i++)
        {
            if (src[from + i] == target)
                return i;
        }
        return -1;
    }

    private static int FindLastFittingSpace(string src, int from, int len, float maxWidth)
    {
        // Walk backwards from end finding a space; verify the prefix width fits.
        for (var i = len - 1; i > 0; i--)
        {
            if (src[from + i] != ' ')
                continue;
            var prefix = src.Substring(from, i);
            if (ImGui.CalcTextSize(prefix).X <= maxWidth)
                return i;
        }

        return -1;
    }

    private static int FindForcedBreak(string src, int from, int len, float maxWidth)
    {
        // Find the rightmost char index whose prefix still fits.
        for (var i = len - 1; i > 0; i--)
        {
            var prefix = src.Substring(from, i);
            if (ImGui.CalcTextSize(prefix).X <= maxWidth)
                return i;
        }

        return -1;
    }

    private static void AddFragment(LayoutResult result, string text, RunStyle style, int urlIndex)
    {
        if (text.Length == 0)
            return;
        result.Fragments.Add(new Fragment(text, style, urlIndex));
    }

    private static void BreakLine(LayoutResult result, Line line, float lineHeight,
                                  ref float y, ref float penX, float contentLeft,
                                  ref bool firstVisualLineOfLogical,
                                  ref int fragmentStart)
    {
        FlushVisualLine(result, line, lineHeight, ref y,
                        ref firstVisualLineOfLogical, ref fragmentStart);
        penX = contentLeft;
    }

    private static void FlushVisualLine(LayoutResult result, Line line, float lineHeight,
                                        ref float y, ref bool firstVisualLineOfLogical,
                                        ref int fragmentStart)
    {
        var fragmentCount = result.Fragments.Count - fragmentStart;
        var drawMarker = firstVisualLineOfLogical
                         && line.Kind is LineKind.Bullet or LineKind.Numbered;
        result.Boxes.Add(new LineBox
                            {
                                Y = y,
                                H = lineHeight,
                                Kind = line.Kind,
                                IndentLevel = line.IndentLevel,
                                DrawMarker = drawMarker,
                                MarkerText = drawMarker ? BuildMarkerText(line) : null,
                                FragmentStart = fragmentStart,
                                FragmentCount = fragmentCount,
                            });
        y += lineHeight;
        firstVisualLineOfLogical = false;
        fragmentStart = result.Fragments.Count;
    }

    private static string? BuildMarkerText(Line line)
    {
        if (line.Kind == LineKind.Bullet)
            return "•";

        // Cache short numbers to avoid repeated allocations across rebuilds.
        var n = line.Number;
        if (n >= 0 && n < _numberedMarkers.Length)
        {
            var s = _numberedMarkers[n];
            if (s == null)
            {
                s = n + ".";
                _numberedMarkers[n] = s;
            }
            return s;
        }

        return n + ".";
    }

    private static readonly string?[] _numberedMarkers = new string?[100];

    private static float MeasureLineHeight(ImFontPtr font)
    {
        ImGui.PushFont(font);
        var h = ImGui.GetTextLineHeight();
        ImGui.PopFont();
        return h;
    }

    private static ImFontPtr ResolveBodyFont(in MarkdownView.Options opt)
    {
        unsafe
        {
            return opt.BodyFont.NativePtr != null ? opt.BodyFont : Fonts.FontNormal;
        }
    }

    private static ImFontPtr ResolveOpRefFont(in MarkdownView.Options opt)
    {
        unsafe
        {
            return opt.OpRefFont.NativePtr != null ? opt.OpRefFont : Fonts.FontNormal;
        }
    }
}
