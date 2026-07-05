#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.SystemUi;

namespace T3.Editor.Gui.Styling.Markdown;

/// <summary>
/// Renders a small, fixed Markdown subset inside an ImGui window. One instance per host
/// window: the cache holds the last parsed/laid-out document and is invalidated when the
/// source text, wrap width, or UI scale changes.
/// </summary>
internal sealed class MarkdownView
{
    public delegate void UrlClicked(string url);

    /// <summary>
    /// Called once per rendered <c>[OpName]</c> fragment, every frame, after
    /// the fragment is drawn. The implementation can inspect the last item
    /// (<see cref="ImGui.IsItemHovered"/>, drag handlers, tooltips, ...) and
    /// attach whatever interaction is appropriate. Without a callback, op-refs
    /// only show a hand cursor on hover.
    /// </summary>
    public delegate void OperatorRefRendered(string opName);

    /// <summary>
    /// Returns the text color for a rendered <c>[OpName]</c> fragment — e.g. the operator's output-type
    /// color. Returning the body text color makes an unknown reference read as plain text. Without a
    /// resolver, op-refs use the default link accent.
    /// </summary>
    public delegate Color OperatorRefColor(string opName);

    public struct Options
    {
        /// <summary>0 = use ContentRegionAvail.X each frame.</summary>
        public float WrapWidthPx { get; set; }
        public float IndentPx { get; set; }

        /// <summary>
        /// Override the body font (used for Paragraph, Bullet, Numbered lines).
        /// Default = unset → falls back to <see cref="Fonts.FontNormal"/>.
        /// Inline `bold` and `code` still use <see cref="Fonts.FontBold"/> /
        /// <see cref="Fonts.Code"/>, which can look size-mismatched if the
        /// body font is larger than them.
        /// </summary>
        public ImFontPtr BodyFont { get; set; }

        /// <summary>
        /// Override the font for inline <c>[OpName]</c> references.
        /// Default = unset → falls back to <see cref="Fonts.FontNormal"/>, which can look
        /// size-mismatched if <see cref="BodyFont"/> is larger (e.g. <see cref="Fonts.FontLarge"/>).
        /// </summary>
        public ImFontPtr OpRefFont { get; set; }

        /// <summary>
        /// Treat every newline in the source as a hard line break instead of
        /// the CommonMark default (single newline within a paragraph = space).
        /// Useful for legacy content (e.g. TourPoint descriptions) authored
        /// expecting each `\n` to start a new line.
        /// </summary>
        public bool HardLineBreaks { get; set; }

        /// <summary>
        /// Render body text with <see cref="UiColors.TextMuted"/> instead of
        /// <see cref="UiColors.Text"/> — for secondary content like parameter descriptions.
        /// </summary>
        public bool MutedBodyText { get; set; }
    }

    public MarkdownView(in Options options)
    {
        _options = options;
    }

    /// <param name="wrapWidthPx">When &gt; 0, overrides both <see cref="Options.WrapWidthPx"/> and the
    /// content-region default with a fixed wrap width. Use this inside auto-resizing windows (tooltips) where
    /// the live content region briefly fluctuates with a transient scrollbar and would otherwise reflow.</param>
    public void Draw(string markdown,
                     UrlClicked? onUrl = null,
                     OperatorRefRendered? onOperatorRef = null,
                     OperatorRefColor? operatorColor = null,
                     float wrapWidthPx = 0)
    {
        if (string.IsNullOrEmpty(markdown))
            return;

        _operatorColor = operatorColor;

        var availableWrap = wrapWidthPx > 0
                                ? wrapWidthPx
                                : _options.WrapWidthPx > 0
                                    ? _options.WrapWidthPx
                                    : ImGui.GetContentRegionAvail().X;

        if (availableWrap < 32f)
            availableWrap = 32f;

        var scale = T3Ui.UiScaleFactor;

        if (!ReferenceEquals(_cachedSource, markdown)
            || Math.Abs(_cachedWrap - availableWrap) > 0.5f
            || Math.Abs(_cachedScale - scale) > 0.001f)
        {
            Rebuild(markdown, availableWrap);
            _cachedSource = markdown;
            _cachedWrap = availableWrap;
            _cachedScale = scale;
        }

        Render(onUrl, onOperatorRef);
    }

    private void Rebuild(string source, float wrapWidthPx)
    {
        MarkdownParser.Parse(source, _parsed, _options.HardLineBreaks);
        MarkdownLayout.Build(_parsed, _layout, _options, wrapWidthPx);
    }

    private void Render(UrlClicked? onUrl, OperatorRefRendered? onOperatorRef)
    {
        // Scope item ids to this view: several views can share a window (e.g. the operator help's
        // description and parameter views), and fragment indices repeat across them.
        ImGui.PushID(GetHashCode());

        var origin = ImGui.GetCursorPos();
        var scale = T3Ui.UiScaleFactor;
        var indentPx = _options.IndentPx > 0 ? _options.IndentPx * scale : 14f * scale;
        var markerPx = 16f * scale;

        for (var bi = 0; bi < _layout.Boxes.Count; bi++)
        {
            var box = _layout.Boxes[bi];

            // Marker for first visual line of a list item.
            if (box.DrawMarker)
            {
                ImGui.SetCursorPos(new Vector2(origin.X + box.IndentLevel * indentPx,
                                               origin.Y + box.Y));
                DrawMarker(box);
            }

            // Position cursor at content start for this visual line.
            var contentLeftPx = box.Kind is LineKind.Bullet or LineKind.Numbered
                                    ? box.IndentLevel * indentPx + markerPx
                                    : 0f;
            ImGui.SetCursorPos(new Vector2(origin.X + contentLeftPx, origin.Y + box.Y));

            DrawLineFragments(box, onUrl, onOperatorRef);
        }

        // Reserve total height so subsequent ImGui content flows below.
        ImGui.SetCursorPos(new Vector2(origin.X, origin.Y + _layout.TotalHeight));
        ImGui.Dummy(new Vector2(1, 1));

        ImGui.PopID();
    }

    private static void DrawMarker(LineBox box)
    {
        if (box.MarkerText == null)
            return;
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(box.MarkerText);
        ImGui.PopStyleColor();
    }

    private void DrawLineFragments(LineBox box, UrlClicked? onUrl, OperatorRefRendered? onOperatorRef)
    {
        // Push the line-level font and color (heading vs body).
        var (lineFont, lineColor) = LineStyle(box.Kind);
        ImGui.PushFont(lineFont);
        ImGui.PushStyleColor(ImGuiCol.Text, lineColor.Rgba);

        var lineOriginY = ImGui.GetCursorPosY();
        var lineFontAscent = lineFont.Ascent;

        for (var fi = 0; fi < box.FragmentCount; fi++)
        {
            var fragment = _layout.Fragments[box.FragmentStart + fi];

            if (fi > 0)
                ImGui.SameLine(0, 0);

            // Per-fragment Y so different fonts (e.g. JetBrainsMono code spans)
            // share a baseline with the line font instead of riding higher.
            var fragmentFont = FragmentFont(fragment, lineFont);
            var fragmentY = lineOriginY + (lineFontAscent - fragmentFont.Ascent);
            ImGui.SetCursorPosY(fragmentY);

            DrawFragment(fragment, box.FragmentStart + fi, onUrl, onOperatorRef);
        }

        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    private ImFontPtr FragmentFont(Fragment fragment, ImFontPtr lineFont)
    {
        if ((fragment.Style & RunStyle.Code) != 0)
            return Fonts.Code;
        if ((fragment.Style & RunStyle.OpRef) != 0)
            return OpRefFont;
        if ((fragment.Style & RunStyle.Bold) != 0)
            return Fonts.FontBold;
        return lineFont;
    }

    private void DrawFragment(Fragment fragment, int fragmentIndex, UrlClicked? onUrl, OperatorRefRendered? onOperatorRef)
    {
        var fontPushed = false;
        var colorPushed = false;
        var isOpRef = (fragment.Style & RunStyle.OpRef) != 0;
        var opRefTopLeft = Vector2.Zero;
        var opRefSize = Vector2.Zero;

        if ((fragment.Style & RunStyle.Code) != 0)
        {
            ImGui.PushFont(Fonts.Code);
            fontPushed = true;
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Fade(0.65f).Rgba);
            colorPushed = true;
        }
        else if ((fragment.Style & RunStyle.Bold) != 0)
        {
            ImGui.PushFont(Fonts.FontBold);
            fontPushed = true;
        }
        else if ((fragment.Style & RunStyle.Link) != 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusActivated.Rgba);
            colorPushed = true;
        }
        else if (isOpRef)
        {
            var opColor = _operatorColor != null && fragment.UrlIndex >= 0
                              ? _operatorColor(_layout.Urls[fragment.UrlIndex])
                              : UiColors.StatusActivated;
            ImGui.PushFont(OpRefFont);
            fontPushed = true;

            var padX = OpRefPaddingX * T3Ui.UiScaleFactor;
            opRefTopLeft = ImGui.GetCursorScreenPos();
            opRefSize = ImGui.CalcTextSize(fragment.Text) + new Vector2(2 * padX, 0);

            // A type-colored box behind the reference, styled like Symbol Library entries.
            // Unresolved refs render as plain body text (see OperatorRefColor) and get no box.
            // The horizontal padding is reserved by the layout pass and the InvisibleButton below.
            if (opColor.Rgba != UiColors.Text.Rgba)
            {
                var isHovered = ImGui.IsMouseHoveringRect(opRefTopLeft, opRefTopLeft + opRefSize);
                var background = isHovered
                                     ? ColorVariations.OperatorBackgroundHover.Apply(opColor)
                                     : ColorVariations.OperatorBackground.Apply(opColor);
                ImGui.GetWindowDrawList().AddRectFilled(opRefTopLeft,
                                                        opRefTopLeft + opRefSize,
                                                        background,
                                                        3 * T3Ui.UiScaleFactor);
                opColor = ColorVariations.OperatorLabel.Apply(opColor);
            }

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + padX);

            ImGui.PushStyleColor(ImGuiCol.Text, opColor.Rgba);
            colorPushed = true;
        }

        ImGui.TextUnformatted(fragment.Text);

        if (colorPushed)
            ImGui.PopStyleColor();
        if (fontPushed)
            ImGui.PopFont();

        // Interaction. Pop styles first so callbacks see the natural item state.
        if ((fragment.Style & RunStyle.Link) != 0 && fragment.UrlIndex >= 0)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    var url = _layout.Urls[fragment.UrlIndex];
                    if (onUrl != null)
                        onUrl(url);
                    else
                        CoreUi.Instance.OpenWithDefaultApplication(url);
                }
            }
        }
        else if (isOpRef)
        {
            // Overlay an invisible button spanning the whole chip (incl. padding): it gives the
            // callback an activatable item for click/drag handling and advances the cursor past
            // the box so the next fragment starts after it, matching the measured layout width.
            ImGui.SetCursorScreenPos(opRefTopLeft);
            ImGui.PushID(fragmentIndex);
            ImGui.InvisibleButton("opRef", opRefSize);
            ImGui.PopID();

            if (fragment.UrlIndex >= 0 && onOperatorRef != null)
            {
                onOperatorRef(_layout.Urls[fragment.UrlIndex]);
            }
            else if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
        }
    }

    private (ImFontPtr font, Color color) LineStyle(LineKind kind)
    {
        return kind switch
                  {
                      LineKind.H1 => (Fonts.FontLarge, UiColors.TextMuted),
                      LineKind.H2 => (Fonts.FontBold, UiColors.Text),
                      LineKind.H3 => (Fonts.FontBold, UiColors.ForegroundFull.Fade(0.8f)),
                      _           => (BodyFont, _options.MutedBodyText ? UiColors.TextMuted : UiColors.Text),
                  };
    }

    private ImFontPtr BodyFont
    {
        get
        {
            unsafe
            {
                return _options.BodyFont.NativePtr != null
                           ? _options.BodyFont
                           : Fonts.FontNormal;
            }
        }
    }

    private ImFontPtr OpRefFont
    {
        get
        {
            unsafe
            {
                return _options.OpRefFont.NativePtr != null
                           ? _options.OpRefFont
                           : Fonts.FontNormal;
            }
        }
    }

    /// <summary>Unscaled horizontal padding reserved left and right of an <c>[OpName]</c> box.</summary>
    internal const float OpRefPaddingX = 3f;

    private readonly Options _options;
    private OperatorRefColor? _operatorColor;
    private readonly ParsedDoc _parsed = new();
    private readonly LayoutResult _layout = new();

    private string? _cachedSource;
    private float _cachedWrap;
    private float _cachedScale;
}
