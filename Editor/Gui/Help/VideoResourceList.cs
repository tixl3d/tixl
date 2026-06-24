#nullable enable
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
/// metadata tooltip on hover. The section heading adapts to which video types are present.
/// </summary>
internal sealed class VideoResourceList
{
    /// <param name="operatorFullPath">The operator's namespace-qualified name, the <c>op:</c> mention key without the prefix.</param>
    internal void Draw(string operatorFullPath)
    {
        var ranked = GetRanked(operatorFullPath);
        if (ranked.Count == 0)
            return;

        FormInputs.AddVerticalSpace(8);
        CustomComponents.StylizedText(SectionHeader(ranked), Fonts.FontSmall, UiColors.TextMuted);
        FormInputs.AddVerticalSpace(3);

        ImGui.PushFont(Fonts.FontSmall);

        var shownCount = _showAll ? ranked.Count : Math.Min(CollapsedCount, ranked.Count);
        for (var i = 0; i < shownCount; i++)
        {
            DrawRow(ranked[i], i);
        }

        if (ranked.Count > CollapsedCount)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
            var moreLabel = _showAll ? "Show less" : $"Show all {ranked.Count}";
            if (ImGui.Selectable(moreLabel))
                _showAll = !_showAll;
            ImGui.PopStyleColor();
        }

        ImGui.PopFont();
    }

    /// <summary>Resets the expansion state when the help topic changes, so a new operator starts collapsed.</summary>
    internal void Reset() => _showAll = false;

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

    private void DrawRow(ScoredSegment scored, int index)
    {
        var segment = scored.Segment;
        HelpIndex.TryGetVideo(segment.VideoId, out var video);

        var typeLabel = TypeLabel(video?.Type);
        var rowColor = scored.IsDeEmphasized ? UiColors.StatusAutomated.Fade(0.5f) : UiColors.StatusAutomated;

        var ageText = video?.Date != null ? ", " + FormatAge(video.Date.Value) : "";
        var label = $"     {typeLabel} ({FormatSegmentDuration(segment.DurationSeconds)}{ageText})";

        ImGui.PushStyleColor(ImGuiCol.Text, rowColor.Rgba);
        ImGui.PushID(index);
        var clicked = ImGui.Selectable(label);
        ImGui.PopID();
        ImGui.PopStyleColor();

        // Capture hover on the full-width row before the cue is drawn — the cue goes straight to the draw
        // list precisely so it doesn't add an item that would steal the row's hover.
        var rowHovered = ImGui.IsItemHovered();
        Icons.DrawIconOnLastItem(Icon.PlayOutput, rowColor, 0);

        if (scored.PredatesCurrentUi)
        {
            const string cue = "predates current UI";
            var cueSize = ImGui.CalcTextSize(cue);
            var rectMin = ImGui.GetItemRectMin();
            var rectMax = ImGui.GetItemRectMax();
            var cuePos = new Vector2(rectMax.X - cueSize.X - 4 * T3Ui.UiScaleFactor,
                                     rectMin.Y + (rectMax.Y - rectMin.Y - cueSize.Y) * 0.5f);
            ImGui.GetWindowDrawList().AddText(cuePos, UiColors.TextMuted.Fade(0.6f), cue);
        }

        if (clicked && !string.IsNullOrEmpty(segment.Url))
            CoreUi.Instance.OpenWithDefaultApplication(segment.Url);

        if (rowHovered)
            DrawRowTooltip(segment, video);
    }

    private void DrawRowTooltip(HelpIndex.OnlineVideoSegment segment, HelpIndex.VideoInfo? video)
    {
        var scale = T3Ui.UiScaleFactor;
        ImGui.SetNextWindowSizeConstraints(new Vector2(320 * scale, 0), new Vector2(360 * scale, ImGui.GetMainViewport().WorkSize.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 8) * scale);
        ImGui.BeginTooltip();

        // Relevancy axes header: e.g. "1 MIN · IN-DEPTH · EXPERIMENT · YOUTUBE"
        var header = FormatSegmentDuration(segment.DurationSeconds).ToUpperInvariant();
        if (!string.IsNullOrEmpty(segment.Depth))
            header += " · " + segment.Depth!.ToUpperInvariant();
        if (!string.IsNullOrEmpty(segment.Style))
            header += " · " + segment.Style!.ToUpperInvariant();
        header += " · YOUTUBE";

        CustomComponents.StylizedText(header, Fonts.FontSmall, UiColors.TextMuted);

        if (video != null)
        {
            FormInputs.AddVerticalSpace(2);
            ImGui.PushFont(Fonts.FontBold);
            ImGui.TextWrapped(video.Title);
            ImGui.PopFont();

            if (video.Date != null)
            {
                var fullLength = video.DurationSeconds > 0 ? FormatClock(video.DurationSeconds) + " · " : "";
                CustomComponents.StylizedText($"{fullLength}{video.Date.Value:MMM yyyy}", Fonts.FontSmall, UiColors.TextMuted);
            }
        }

        if (!string.IsNullOrEmpty(segment.Note))
        {
            FormInputs.AddVerticalSpace(4);
            // [OpName] refs in the note render as colored labels for display; they suppress their own
            // tooltip so they don't nest inside this one.
            _noteView.Draw(segment.Note,
                           onOperatorRef: static op => MarkdownOperatorLinks.HandleOperatorRef(op, suppressTooltip: true),
                           operatorColor: MarkdownOperatorLinks.GetOperatorColor,
                           wrapWidthPx: 340 * scale);
        }

        FormInputs.AddVerticalSpace(4);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        var openAt = video != null ? $"Open at {FormatClock(segment.StartSecond)} in your browser" : "Open in your browser";
        ImGui.TextUnformatted(openAt);
        ImGui.PopStyleColor();

        ImGui.EndTooltip();
        ImGui.PopStyleVar();
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

    private static string FormatAge(DateTime date)
    {
        var days = (DateTime.UtcNow - date).TotalDays;
        if (days < 21)
            return "recently";

        if (days < 60)
            return $"{(int)Math.Round(days / 30.4)} months ago";

        var years = days / 365.25;
        if (years >= 1)
        {
            var wholeYears = (int)Math.Round(years);
            return wholeYears <= 1 ? "1 year ago" : $"{wholeYears} years ago";
        }

        return $"{(int)Math.Round(days / 30.4)} months ago";
    }

    private readonly record struct ScoredSegment(
        HelpIndex.OnlineVideoSegment Segment,
        float Score,
        bool PredatesCurrentUi,
        bool IsDeEmphasized);

    private const int CollapsedCount = 2;
    private const float PredatesCurrentUiYears = 2f;

    private bool _showAll;
    private readonly Dictionary<string, IReadOnlyList<ScoredSegment>> _rankedCache = new();
    private readonly MarkdownView _noteView = new(new MarkdownView.Options());
}
