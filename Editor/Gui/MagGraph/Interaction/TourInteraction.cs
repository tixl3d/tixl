using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;
using SkillTraining = T3.Editor.Skills.Training.SkillTraining;

namespace T3.Editor.Gui.MagGraph.Interaction;

/// <summary>
/// Displays and handles the navigation through tour points
/// </summary>
internal static class TourInteraction
{
    internal static void Draw(ProjectView projectView)
    {
        if (projectView == null || projectView.GraphView.Destroyed)
            return;

        if (projectView.CompositionInstance == null)
            return;

        _symbolTourProgress ??= new Dictionary<Guid, int>();

        var compositionUi = projectView.CompositionInstance.Symbol.GetSymbolUi();
        if (compositionUi.TourPoints.Count == 0)
            return;

        UpdateStyles();
        if (ImGui.IsWindowAppearing())
        {
            _lastClickTime = ImGui.GetTime();
        }

        SkillTraining.DrawLevelHeader();

        FormInputs.AddVerticalSpace();

        var cursorPos = ImGui.GetCursorScreenPos();
        ImGui.Indent(40 * T3Ui.UiScaleFactor - 5);

        if (!_symbolTourProgress.TryGetValue(compositionUi.Symbol.Id, out var progressIndex))
        {
            progressIndex = 0;
        }

        var completed = progressIndex < 0 || progressIndex >= compositionUi.TourPoints.Count;

        if (completed)
        {
            if (CustomComponents.DrawCtaButton("Restart Tour", Icon.None, UiColors.TextMuted, Color.Transparent, UiColors.ForegroundFull.Fade(0.2f)))
                SetProgressIndex(compositionUi, 0);
        }
        else
        {
            var dl = ImGui.GetWindowDrawList();
            var activeTourPoint = compositionUi.TourPoints[progressIndex];

            if (DrawProgressDots(dl, compositionUi, cursorPos, progressIndex, out var hoveredIndex))
            {
                if (hoveredIndex >= 0)
                    SetProgressIndex(compositionUi, hoveredIndex);
            }

            RenderTourPointDetails(compositionUi, progressIndex, hoveredIndex);
            DrawGraphIndicator(dl, activeTourPoint, projectView, compositionUi);
        }
    }

    private static void RenderTourPointDetails(SymbolUi compositionUi, int progressIndex, int hoveredIndex)
    {
        if (compositionUi.TourPoints.Count == 0)
            return;

        var isDotHovered = hoveredIndex >= 0 && hoveredIndex != progressIndex;
        var activeIndex = (isDotHovered ? hoveredIndex : progressIndex).Clamp(0, compositionUi.TourPoints.Count - 1);
        var isLast = activeIndex == compositionUi.TourPoints.Count - 1;
        var activeTourPoint = compositionUi.TourPoints[activeIndex];

        var style = _windowStyles[int.Min(_windowStyles.Length - 1, (int)activeTourPoint.Style)];

        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 10);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 6) * T3Ui.UiScaleFactor);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, style.WindowBackground.Rgba);
        ImGui.PushStyleColor(ImGuiCol.Border, style.WindowBorder.Rgba);

        if (ImGui.BeginChild("Task", new Vector2(500 * T3Ui.UiScaleFactor, _lastContentHeight), ImGuiChildFlags.Borders | ImGuiChildFlags.AlwaysUseWindowPadding,
                             ImGuiWindowFlags.NoScrollbar))
        {
            // Draw task
            ImGui.SameLine(0, 4);

            var typeWriterProgress = (TimeSinceInteraction * 200f / (activeTourPoint.Description.Length + 1)).Clamp(0, 1);

            if (!string.IsNullOrEmpty(activeTourPoint.Description))
            {
                // Use the original description string once typing is done so
                // MarkdownView's reference-equality cache stays warm. During
                // typing we allocate a sliced snapshot per frame — brief and
                // bounded by the description length.
                var textToRender = typeWriterProgress >= 1f
                                       ? activeTourPoint.Description
                                       : StringUtils.SliceToProgress(activeTourPoint.Description,
                                                                      typeWriterProgress).ToString();

                EnsureMarkdownView();
                _markdown.Draw(textToRender);
            }

            if (typeWriterProgress >= 1 && !isDotHovered)
            {
                FormInputs.AddVerticalSpace();

                if (CustomComponents.DrawCtaButton(style.ButtonLabel,
                                     isLast ? Icon.None : Icon.ArrowRight,
                                     textColor: style.ButtonLabelColor,
                                     bgColor: style.ButtonBackground,
                                     borderColor: style.ButtonBorder))
                {
                    SetProgressIndex(compositionUi, activeIndex + 1);
                }
            }

            FormInputs.AddVerticalSpace();
            _lastContentHeight = ImGui.GetCursorPosY();
        }
        ImGui.EndChild();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private static bool DrawProgressDots(ImDrawListPtr dl, SymbolUi compositionUi, Vector2 pos, int activeIndex, out int hoveredIndex)
    {
        var clicked = false;
        hoveredIndex = -1;
        var tourPointsCount = compositionUi.TourPoints.Count;
        var windowsHovered = ImGui.IsWindowHovered();
        var mousePos = ImGui.GetMousePos();

        var h = ImGui.GetFrameHeight();
        pos += new Vector2(0.9f, 0.3f) * h;

        var size = MathF.Floor(14 * T3Ui.UiScaleFactor);

        var spacing = new Vector2(0, size);

        for (var dotIndex = 0; dotIndex < tourPointsCount; dotIndex++)
        {
            var dotPos = pos + spacing * dotIndex;

            var isHovered = hoveredIndex == -1 && windowsHovered && Vector2.Distance(mousePos, dotPos) < size / 2;
            if (isHovered)
            {
                dl.AddCircle(dotPos, size * 0.6f, UiColors.ForegroundFull.Fade(0.5f));
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    clicked = true;
                }

                hoveredIndex = dotIndex;
            }

            var isCurrent = dotIndex == activeIndex;
            var radius = 3 + (isCurrent ? 2 + 5 * AttentionScaleFactor : 0);

            var color = isCurrent ? UiColors.BackgroundActive : UiColors.ForegroundFull.Fade(0.3f);
            dl.AddCircleFilled(dotPos, radius, color, 16);
        }

        return clicked;
    }

    private static void DrawGraphIndicator(ImDrawListPtr dl, TourPoint activeTourPoint, ProjectView projectView,
                                           SymbolUi compositionUi)
    {
        if (activeTourPoint.Style == TourPoint.Styles.Info)
            return;

        if (!compositionUi.ChildUis.TryGetValue(activeTourPoint.ChildId, out var child))
            return;

        if (_lastCompositionId != compositionUi.Symbol.Id)
        {
            _lastCompositionId = compositionUi.Symbol.Id;
            _dampedCanvasPos = child.PosOnCanvas;
        }
        else
        {
            _dampedCanvasPos = Vector2.Lerp(_dampedCanvasPos, child.PosOnCanvas, 0.1f);
        }

        var posOnScreen = projectView.GraphView.Canvas.TransformPosition(_dampedCanvasPos);

        var fadeCount = 4;
        var t = ImGui.GetTime();

        var dotRadius = 40 + (100 / (TimeSinceInteraction * 0.5f + 1));

        for (int fadeIndex = 0; fadeIndex < fadeCount; fadeIndex++)
        {
            var xx = (float)((t * 0.1f + fadeIndex / (float)fadeCount) % 1);
            xx = MathF.Pow(1 - xx, 2.5f);

            dl.AddCircleFilled(posOnScreen, (1 - xx) * dotRadius, UiColors.BackgroundActive.Fade(0.3f * xx));
        }
    }

    private sealed record Style(
        Color WindowBorder,
        Color WindowBackground,
        Color ButtonBorder,
        Color ButtonBackground,
        Color ButtonLabelColor,
        string ButtonLabel);

    private static void UpdateStyles()
    {
        // if (!FrameStats.Last.UiColorsChanged && _windowStyles != null)
        //     return;

        _windowStyles ??= new Style[Enum.GetNames<TourPoint.Styles>().Length];

        var solidBackground = Color.Mix(UiColors.WindowBackground, UiColors.BackgroundFull, 0.6f);
        _windowStyles[(int)TourPoint.Styles.Info]
            = new Style(UiColors.BackgroundActive,
                        solidBackground,
                        Color.Transparent,
                        UiColors.BackgroundActive,
                        UiColors.ForegroundFull,
                        "Continue Reading..."
                       );

        _windowStyles[(int)TourPoint.Styles.InfoFor]
            = new Style(UiColors.ForegroundFull.Fade(0.5f),
                        solidBackground,
                        Color.Transparent,
                        UiColors.BackgroundActive,
                        UiColors.ForegroundFull,
                        "Got it"
                       );

        // Something close to background color, but opaque enough to ensure readability...
        var dimmedBackground = Color.Mix(UiColors.WindowBackground, UiColors.BackgroundFull, 0.6f).Fade(0.8f);

        _windowStyles[(int)TourPoint.Styles.CallToAction]
            = new Style(UiColors.BackgroundFull.Fade(0.6f),
                        dimmedBackground,
                        UiColors.ForegroundFull.Fade(0.4f),
                        Color.Transparent,
                        UiColors.ForegroundFull,
                        "Done"
                       );

        _windowStyles[(int)TourPoint.Styles.Conclusion]
            = new Style(UiColors.BackgroundFull.Fade(0.6f),
                        dimmedBackground,
                        UiColors.ForegroundFull.Fade(0.4f),
                        Color.Transparent,
                        UiColors.ForegroundFull,
                        "Okay"
                       );

        _windowStyles[(int)TourPoint.Styles.Tip]
            = new Style(UiColors.BackgroundFull.Fade(0.6f),
                        dimmedBackground,
                        UiColors.ForegroundFull.Fade(0.4f),
                        Color.Transparent,
                        UiColors.ForegroundFull,
                        "Got it"
                       );
    }

    private static void EnsureMarkdownView()
    {
        if (_markdown != null)
            return;
        _markdown = new MarkdownView(new MarkdownView.Options
                                          {
                                              BodyFont = Fonts.FontLarge,
                                              // Keep op links at body size — the default FontBold
                                              // would render them visibly smaller than FontLarge.
                                              OpRefFont = Fonts.FontLarge,
                                              // TourPoint descriptions historically treat each \n as a
                                              // hard line break (used as paragraph separators by the
                                              // legacy paragraph-renderer); preserve that here.
                                              HardLineBreaks = true,
                                              WrapWidthPx = 470 * T3Ui.UiScaleFactor,
                                          });
    }

    private static void SetProgressIndex(SymbolUi symbolUi, int index)
    {
        _symbolTourProgress[symbolUi.Symbol.Id] = index;
        _lastClickTime = ImGui.GetTime();
    }

    private static float _lastContentHeight;
    private static Style[] _windowStyles;

    private static Vector2 _dampedCanvasPos;
    private static Guid _lastCompositionId;
    private static double _lastClickTime;
    private static float TimeSinceInteraction => (float)(ImGui.GetTime() - _lastClickTime).ClampMin(0);
    private static float AttentionScaleFactor => (1 / (TimeSinceInteraction * 3f + 1)).Clamp(0, 1);

    public static void SetProgressIndex(Guid compositionId, int index)
    {
        _symbolTourProgress[compositionId] = index;
    }

    /// <summary>
    /// True while the user is reading an introductory tour point (Info / InfoFor) on this
    /// composition. Used by SkillQuest feedback to suppress hint icons while the tour owns
    /// the user's attention. Call-to-Action, Conclusion, and Tip points expect the user to
    /// engage with the graph, so they don't block hints.
    /// </summary>
    public static bool IsTourBlockingHints(SymbolUi compositionUi)
    {
        if (compositionUi.TourPoints.Count == 0)
            return false;

        if (_symbolTourProgress == null)
            return true;

        if (!_symbolTourProgress.TryGetValue(compositionUi.Symbol.Id, out var progressIndex))
            return true;

        if (progressIndex < 0 || progressIndex >= compositionUi.TourPoints.Count)
            return false;

        var style = compositionUi.TourPoints[progressIndex].Style;
        return style == TourPoint.Styles.Info || style == TourPoint.Styles.InfoFor;
    }

    // -1 means completed or hidden
    private static Dictionary<Guid, int> _symbolTourProgress = new();

    private static MarkdownView _markdown;
}