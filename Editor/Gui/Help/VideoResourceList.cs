#nullable enable
using System.Globalization;
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.SystemUi;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.Styling.Markdown;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Draws the video-resource list below an operator's help body: the topic's
/// <see cref="HelpIndex.OnlineVideoSegment"/>s ranked by relevancy (depth × trust × confidence × age), the
/// top couple shown with a "Show all N" toggle, each row opening the video at its timestamp and revealing a
/// thumbnail tooltip on hover. The section heading adapts to which video types are present.
/// </summary>
internal sealed class VideoResourceList
{
    /// <param name="operatorFullPath">The operator's namespace-qualified name, the <c>op:</c> mention key without the prefix.</param>
    internal void Draw(string operatorFullPath)
    {
        _tooltipDrawn = false;

        var ranked = GetRanked(operatorFullPath);
        if (ranked.Count == 0)
            return;

        FormInputs.AddVerticalSpace(5);
        CustomComponents.StylizedText(SectionHeader(ranked).ToUpperInvariant(), Fonts.FontSmall, UiColors.TextMuted);
        FormInputs.AddVerticalSpace(3);

        var rowHeight = RowHeight();
        var shownCount = _showAll ? ranked.Count : Math.Min(CollapsedCount, ranked.Count);
        for (var i = 0; i < shownCount; i++)
        {
            DrawRow(ranked[i], i, rowHeight);
        }

        if (ranked.Count > CollapsedCount)
        {
            ImGui.PushFont(Fonts.FontSmall);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            var moreLabel = _showAll ? "Show less" : $"Show all {ranked.Count}";
            if (ImGui.Selectable(moreLabel))
                _showAll = !_showAll;
            ImGui.PopStyleColor();
            ImGui.PopFont();
        }
    }

    /// <summary>Resets the expansion state when the help topic changes, so a new operator starts collapsed.</summary>
    internal void Reset() => _showAll = false;

    /// <summary>
    /// Pixel height the list will occupy for the operator (0 if it has no resources), so the Help window can
    /// dock it as a fixed footer. Capped at half <paramref name="maxHeight"/> — beyond that the footer scrolls.
    /// </summary>
    internal float MeasureHeight(string operatorFullPath, float maxHeight)
    {
        var ranked = GetRanked(operatorFullPath);
        if (ranked.Count == 0)
            return 0;

        ImGui.PushFont(Fonts.FontSmall);
        var smallLine = ImGui.GetTextLineHeightWithSpacing();
        ImGui.PopFont();

        var rowHeight = RowHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var topPadding = 5;
        var rowCount = _showAll ? ranked.Count : Math.Min(CollapsedCount, ranked.Count);
        var showAllRow = ranked.Count > CollapsedCount ? smallLine : 0;

        var scale = T3Ui.UiScaleFactor;
        var height = ImGui.GetStyle().WindowPadding.Y * 2 // the footer child's own top/bottom padding
                     + 3 * scale + smallLine               // heading + the AddVerticalSpace under it
                     + rowCount * (rowHeight + spacing)
                     + topPadding
                     + showAllRow;
        return Math.Min(height, maxHeight * 0.5f);
    }

    /// <summary>Names the section after the kinds of video present — specific when they're all one type, generic when mixed.</summary>
    private static string SectionHeader(IReadOnlyList<ScoredSegment> ranked)
    {
        bool hasTutorial = false, hasMeetup = false, hasUpdate = false, hasOther = false;
        foreach (var scored in ranked)
        {
            HelpIndex.TryGetVideo(scored.Segment.VideoId, out var video);
            switch (video?.Type)
            {
                case "tutorial": hasTutorial = true; break;
                case "meetup":   hasMeetup = true; break;
                case "update":   hasUpdate = true; break;
                default:         hasOther = true; break;
            }
        }

        var distinctKinds = (hasTutorial ? 1 : 0) + (hasMeetup ? 1 : 0) + (hasUpdate ? 1 : 0) + (hasOther ? 1 : 0);
        if (distinctKinds == 1)
        {
            if (hasTutorial)
                return "Related tutorials";
            if (hasMeetup)
                return "Discussed in meet-ups";
            if (hasUpdate)
                return "Mentioned in release videos";
        }

        return "Watch & learn";
    }

    private void DrawRow(ScoredSegment scored, int index, float rowHeight)
    {
        var segment = scored.Segment;
        HelpIndex.TryGetVideo(segment.VideoId, out var video);

        var scale = T3Ui.UiScaleFactor;
        var width = ImGui.GetContentRegionAvail().X;

        ImGui.PushID(index);
        ImGui.InvisibleButton("##row", new Vector2(width, rowHeight));
        ImGui.PopID();

        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();

        // Hover affordance matches the Asset Library folder rows: a soft rounded fill + white text.
        if (hovered)
            drawList.AddRectFilled(min, max, UiColors.BackgroundActive.Fade(0.2f), 5 * scale);

        var accentColor = UiColors.StatusActivated.Fade(hovered ? 1:0.8f);

        var linkColor = Color.Mix(UiColors.StatusActivated, UiColors.ForegroundFull, 0.3f);
        var reactiveLinkColor = linkColor.Fade(hovered ? 1:0.8f);
        //var annotationColor = hovered ? UiColors.ForegroundFull.Fade(0.8f) : UiColors.TextMuted;

        Icons.GetGlyphDefinition(Icon.PlayOutput, out _, out var iconSize);
        var iconPos = new Vector2(min.X + 4 * scale, min.Y + (rowHeight - iconSize.Y) * 0.5f);
        Icons.DrawIconAtScreenPosition(Icon.PlayOutput, iconPos, drawList, accentColor);

        var textX = min.X + 4 * scale + iconSize.X + 6 * scale;

        ImGui.PushFont(Fonts.FontBold);
        var typeLabel = TypeLabel(video?.Type);
        var typeWidth = ImGui.CalcTextSize(typeLabel).X;
        var textY = min.Y + (rowHeight - ImGui.GetTextLineHeight()) * 0.5f;
        drawList.AddText(new Vector2(textX, textY), reactiveLinkColor, typeLabel);
        ImGui.PopFont();

        var annotation = RowAnnotation(segment);
        ImGui.PushFont(Fonts.FontNormal);
        drawList.AddText(new Vector2(textX + typeWidth + 6 * scale, textY), reactiveLinkColor, annotation);
        ImGui.PopFont();

        if (scored.PredatesCurrentUi)
        {
            ImGui.PushFont(Fonts.FontSmall);
            const string cue = "predates current UI";
            var cueSize = ImGui.CalcTextSize(cue);
            drawList.AddText(new Vector2(max.X - cueSize.X - 6 * scale, min.Y + (rowHeight - cueSize.Y) * 0.5f),
                             UiColors.TextMuted.Fade(0.6f), cue);
            ImGui.PopFont();
        }

        if (clicked && !string.IsNullOrEmpty(segment.Url))
            CoreUi.Instance.OpenWithDefaultApplication(segment.Url);

        // Only one tooltip per frame so rapidly crossed rows can't stack their content into one window.
        if (hovered && !_tooltipDrawn)
        {
            _tooltipDrawn = true;
            DrawRowTooltip(segment, video);
        }
    }

    private void DrawRowTooltip(HelpIndex.OnlineVideoSegment segment, HelpIndex.VideoInfo? video)
    {
        var scale = T3Ui.UiScaleFactor;
        var textureId = IntPtr.Zero;
        var aspectRatio = 16f / 9f;
        var hasThumbnail = video != null && VideoThumbnails.TryGetTexture(video.Id, out textureId, out aspectRatio);

        var thumbnailWidth = 150 * scale;
        var textWidth = 260 * scale;
        var padding = 10 * scale;
        var tooltipWidth = (hasThumbnail ? thumbnailWidth + 10 * scale : 0) + textWidth + padding * 2;

        ImGui.SetNextWindowSizeConstraints(new Vector2(tooltipWidth, 0), new Vector2(tooltipWidth, ImGui.GetMainViewport().WorkSize.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, 9 * scale));
        ImGui.BeginTooltip();

        if (hasThumbnail)
        {
            ImGui.BeginGroup();
            DrawThumbnail(textureId, aspectRatio, thumbnailWidth, video!);
            ImGui.EndGroup();
            ImGui.SameLine(0, 10 * scale);
        }

        ImGui.BeginGroup();

        // e.g. "5MIN ON YOUTUBE / SEP 2024"
        var header = FormatSegmentDuration(segment.DurationSeconds).ToUpperInvariant() + " ON YOUTUBE";
        if (video?.Date != null)
            header += " / " + video.Date.Value.ToString("MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();
        CustomComponents.StylizedText(header, Fonts.FontSmall, UiColors.TextMuted);

        if (video != null && !string.IsNullOrEmpty(video.Title))
        {
            FormInputs.AddVerticalSpace(2);
            ImGui.PushFont(Fonts.FontBold);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.ForegroundFull.Rgba);
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWidth);
            ImGui.TextWrapped(ClampText(video.Title, MaxTitleChars));
            ImGui.PopTextWrapPos();
            ImGui.PopStyleColor();
            ImGui.PopFont();
        }

        if (!string.IsNullOrEmpty(segment.Note))
        {
            FormInputs.AddVerticalSpace(3);
            // [OpName] refs in the note render as colored labels; they suppress their own tooltip so they don't nest.
            _noteView.Draw(segment.Note,
                           onOperatorRef: static op => MarkdownOperatorLinks.HandleOperatorRef(op, suppressTooltip: true),
                           operatorColor: MarkdownOperatorLinks.GetOperatorColor,
                           wrapWidthPx: textWidth);
        }

        ImGui.EndGroup();
        ImGui.EndTooltip();
        ImGui.PopStyleVar();
    }

    private static void DrawThumbnail(IntPtr textureId, float aspectRatio, float width, HelpIndex.VideoInfo video)
    {
        var scale = T3Ui.UiScaleFactor;
        var height = width / (aspectRatio <= 0 ? 16f / 9f : aspectRatio);

        var min = ImGui.GetCursorScreenPos();
        ImGui.Image(textureId, new Vector2(width, height));
        var max = min + new Vector2(width, height);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(min, max, UiColors.BackgroundFull.Fade(0.5f));

        // Play badge.
        var center = (min + max) * 0.5f;
        drawList.AddCircleFilled(center, 15 * scale, UiColors.BackgroundFull.Fade(0.4f));
        var t = 6 * scale;
        drawList.AddTriangleFilled(center + new Vector2(-t * 0.5f, -t),
                                   center + new Vector2(-t * 0.5f, t),
                                   center + new Vector2(t, 0),
                                   UiColors.ForegroundFull);

        // "Tutorial 5:23" — type + full video length, bottom-left, with a shadow so it reads over any image.
        ImGui.PushFont(Fonts.FontSmall);
        var label = TypeLabel(video.Type) + " " + FormatClock(video.DurationSeconds);
        var labelPos = new Vector2(min.X + 6 * scale, max.Y - ImGui.GetTextLineHeight() - 5 * scale);
        drawList.AddText(labelPos + new Vector2(1, 1) * scale, UiColors.BackgroundFull, label);
        drawList.AddText(labelPos, UiColors.ForegroundFull, label);
        ImGui.PopFont();
    }

    private IReadOnlyList<ScoredSegment> GetRanked(string operatorFullPath)
    {
        if (_rankedCache.TryGetValue(operatorFullPath, out var cached))
            return cached;

        var segments = HelpIndex.GetOperatorSegments(operatorFullPath);
        var scored = new List<ScoredSegment>(segments.Count);
        var now = DateTime.UtcNow;
        foreach (var segment in segments)
        {
            HelpIndex.TryGetVideo(segment.VideoId, out var video);
            scored.Add(Score(segment, video, now));
        }

        scored.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        _rankedCache[operatorFullPath] = scored;
        return scored;
    }

    private static ScoredSegment Score(HelpIndex.OnlineVideoSegment segment, HelpIndex.VideoInfo? video, DateTime now)
    {
        var depthScore = segment.Depth switch
                             {
                                 "in-depth"  => 1.0f,
                                 "explained" => 0.7f,
                                 "passing"   => 0.4f,
                                 _           => 0.5f,
                             };

        // Trust order: scripted > answer > discussion > experiment.
        var styleScore = segment.Style switch
                             {
                                 "scripted"   => 1.0f,
                                 "answer"     => 0.85f,
                                 "discussion" => 0.65f,
                                 "experiment" => 0.5f,
                                 _            => 0.65f,
                             };

        var confidenceScore = ((segment.Confidence ?? 65) / 100f).Clamp(0.2f, 1f);

        // Operator math is stable, so age decays gently here (the heavier ui:-topic curve is a Phase 2 concern).
        var yearsOld = video?.Date != null ? (float)(now - video.Date.Value).TotalDays / 365.25f : 1f;
        var ageScore = (1f - yearsOld * 0.07f).Clamp(0.45f, 1f);

        var predatesCurrentUi = yearsOld >= PredatesCurrentUiYears;
        var deEmphasized = segment.Style == "experiment" || (segment.Confidence ?? 65) < 50;

        return new ScoredSegment(segment,
                                 depthScore * styleScore * confidenceScore * ageScore,
                                 predatesCurrentUi,
                                 deEmphasized);
    }

    /// <summary>Compact relevancy line for a row, e.g. <c>(5min · In-depth · Experiment)</c> — duration plus the axes that survive.</summary>
    private static string RowAnnotation(HelpIndex.OnlineVideoSegment segment)
    {
        var annotation = "(" + FormatSegmentDuration(segment.DurationSeconds);
        if (!string.IsNullOrEmpty(segment.Depth))
            annotation += " · " + Capitalize(segment.Depth!);
        if (!string.IsNullOrEmpty(segment.Style))
            annotation += " · " + Capitalize(segment.Style!);
        return annotation + ")";
    }

    private static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    private static string ClampText(string text, int maxChars)
    {
        return text.Length <= maxChars ? text : text.Substring(0, maxChars).TrimEnd() + "...";
    }

    private static float RowHeight()
    {
        ImGui.PushFont(Fonts.FontNormal);
        var height = ImGui.GetTextLineHeight() + 6 * T3Ui.UiScaleFactor;
        ImGui.PopFont();
        return height;
    }

    private static string TypeLabel(string? type)
    {
        return type switch
                   {
                       "meetup"   => "Meet-up",
                       "tutorial" => "Tutorial",
                       "update"   => "Release overview",
                       _          => "Video",
                   };
    }

    private static string FormatSegmentDuration(int seconds)
    {
        if (seconds < 60)
            return $"{seconds}s";

        return $"{(int)Math.Round(seconds / 60f)}min";
    }

    private static string FormatClock(int seconds)
    {
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    private readonly record struct ScoredSegment(
        HelpIndex.OnlineVideoSegment Segment,
        float Score,
        bool PredatesCurrentUi,
        bool IsDeEmphasized);

    private const int CollapsedCount = 2;
    private const float PredatesCurrentUiYears = 2f;
    private const int MaxTitleChars = 84;

    private bool _showAll;
    private bool _tooltipDrawn;
    private readonly Dictionary<string, IReadOnlyList<ScoredSegment>> _rankedCache = new();
    private readonly MarkdownView _noteView = new(new MarkdownView.Options());
}
