#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.SystemUi;

namespace T3.Editor.Gui.Styling.Markdown;

/// <summary>
/// Renders a small, fixed Markdown subset inside an ImGui window. See
/// <see href="../../../../../.agentic/Plans/Plan_MarkdownRenderer.md"/>
/// for the supported syntax and design notes.
///
/// One <see cref="MarkdownView"/> per host window: the cache holds the last
/// parsed/laid-out document and is invalidated when the source text, wrap
/// width, or UI scale changes.
/// </summary>
internal sealed class MarkdownView
{
    public delegate void UrlClicked(string url);

    public delegate void OperatorRefClicked(string opName);

    public struct Options
    {
        /// <summary>0 = use ContentRegionAvail.X each frame.</summary>
        public float WrapWidthPx { get; set; }
        public float IndentPx { get; set; }
    }

    public MarkdownView(in Options options)
    {
        _options = options;
    }

    public void Draw(string markdown,
                     UrlClicked? onUrl = null,
                     OperatorRefClicked? onOperatorRef = null)
    {
        if (string.IsNullOrEmpty(markdown))
            return;

        var availableWrap = _options.WrapWidthPx > 0
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
        MarkdownParser.Parse(source, _parsed);
        MarkdownLayout.Build(_parsed, _layout, _options, wrapWidthPx);
    }

    private void Render(UrlClicked? onUrl, OperatorRefClicked? onOperatorRef)
    {
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
    }

    private static void DrawMarker(LineBox box)
    {
        if (box.MarkerText == null)
            return;
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.TextUnformatted(box.MarkerText);
        ImGui.PopStyleColor();
    }

    private void DrawLineFragments(LineBox box, UrlClicked? onUrl, OperatorRefClicked? onOperatorRef)
    {
        // Push the line-level font and color (heading vs body).
        var (lineFont, lineColor) = LineStyle(box.Kind);
        ImGui.PushFont(lineFont);
        ImGui.PushStyleColor(ImGuiCol.Text, lineColor.Rgba);

        for (var fi = 0; fi < box.FragmentCount; fi++)
        {
            var fragment = _layout.Fragments[box.FragmentStart + fi];
            DrawFragment(fragment, onUrl, onOperatorRef);
            if (fi + 1 < box.FragmentCount)
                ImGui.SameLine(0, 0);
        }

        ImGui.PopStyleColor();
        ImGui.PopFont();
    }

    private void DrawFragment(Fragment fragment, UrlClicked? onUrl, OperatorRefClicked? onOperatorRef)
    {
        var fontPushed = false;
        var colorPushed = false;

        if ((fragment.Style & RunStyle.Code) != 0)
        {
            ImGui.PushFont(Fonts.Code);
            fontPushed = true;
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAutomated.Fade(0.85f).Rgba);
            colorPushed = true;
        }
        else if ((fragment.Style & RunStyle.Bold) != 0)
        {
            ImGui.PushFont(Fonts.FontBold);
            fontPushed = true;
        }
        else if ((fragment.Style & RunStyle.Link) != 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusAutomated.Rgba);
            colorPushed = true;
        }
        else if ((fragment.Style & RunStyle.OpRef) != 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusActivated.Rgba);
            colorPushed = true;
        }

        ImGui.TextUnformatted(fragment.Text);

        var hasInteraction = (fragment.Style & (RunStyle.Link | RunStyle.OpRef)) != 0;
        if (hasInteraction)
        {
            if (ImGui.IsItemHovered())
            {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && fragment.UrlIndex >= 0)
                {
                    var target = _layout.Urls[fragment.UrlIndex];
                    if ((fragment.Style & RunStyle.Link) != 0)
                    {
                        if (onUrl != null)
                            onUrl(target);
                        else
                            CoreUi.Instance.OpenWithDefaultApplication(target);
                    }
                    else if ((fragment.Style & RunStyle.OpRef) != 0)
                    {
                        onOperatorRef?.Invoke(target);
                    }
                }
            }
        }

        if (colorPushed)
            ImGui.PopStyleColor();
        if (fontPushed)
            ImGui.PopFont();
    }

    private static (ImFontPtr font, Color color) LineStyle(LineKind kind)
    {
        return kind switch
                  {
                      LineKind.H1 => (Fonts.FontLarge, UiColors.TextMuted),
                      LineKind.H2 => (Fonts.FontBold, UiColors.Text),
                      LineKind.H3 => (Fonts.FontBold, UiColors.ForegroundFull.Fade(0.8f)),
                      _ => (Fonts.FontNormal, UiColors.Text),
                  };
    }

    private readonly Options _options;
    private readonly ParsedDoc _parsed = new();
    private readonly LayoutResult _layout = new();

    private string? _cachedSource;
    private float _cachedWrap;
    private float _cachedScale;
}
