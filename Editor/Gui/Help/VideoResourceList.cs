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
/// Draws the unified "watch &amp; learn" list below an operator's help body: the hand-authored symbol
/// <see cref="LinkRow"/>s (wiki pages, example links) and the auto-extracted video
/// <see cref="HelpIndex.OnlineVideoSegment"/>s, ranked together (links carry a focus-style boost so they lead),
/// the top few shown with an inline "SHOW ALL N" toggle. Each row opens its target; video rows reveal a
/// thumbnail tooltip on hover.
/// </summary>
internal sealed class VideoResourceList
{
    /// <summary>A hand-authored symbol link (the old "Links:" section), now ranked into the same list.</summary>
    internal readonly record struct LinkRow(string Title, string Url, string Description, Icon? Icon);

    /// <param name="mentionKey">The full mention key (<c>op:&lt;fullpath&gt;</c> or <c>ui:&lt;id&gt;</c>), or null.</param>
    /// <param name="links">Hand-authored symbol links to merge in, ranked above the videos.</param>
    internal void Draw(string? mentionKey, IReadOnlyList<LinkRow> links)
    {
        _tooltipDrawn = false;

        var rows = BuildRows(mentionKey, links);
        if (rows.Count == 0)
            return;

        FormInputs.AddVerticalSpace(5);

        // Heading on the left; the "SHOW ALL N" toggle sits on the same line, right-aligned, small caps, active blue.
        var headerY = ImGui.GetCursorPosY();
        CustomComponents.StylizedText(SectionHeader(rows).ToUpperInvariant(), Fonts.FontSmall, UiColors.TextMuted);
        if (rows.Count > CollapsedCount)
        {
            var afterHeaderY = ImGui.GetCursorPosY();
            ImGui.SetCursorPosY(headerY);
            ImGui.PushFont(Fonts.FontSmall);
            var toggleLabel = _showAll ? "SHOW LESS" : $"SHOW ALL {rows.Count}";
            var toggleWidth = ImGui.CalcTextSize(toggleLabel).X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - toggleWidth);
            ImGui.PushStyleColor(ImGuiCol.Text, UiColors.StatusActivated.Rgba);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, UiColors.BackgroundActive.Fade(0.2f).Rgba);
            if (ImGui.Selectable(toggleLabel, false, ImGuiSelectableFlags.None,
                                 new Vector2(toggleWidth, ImGui.GetTextLineHeight())))
                _showAll = !_showAll;
            ImGui.PopStyleColor(2);
            ImGui.PopFont();
            ImGui.SetCursorPosY(afterHeaderY);
        }
        FormInputs.AddVerticalSpace(3);

        var rowHeight = RowHeight();
        var shownCount = _showAll ? rows.Count : Math.Min(CollapsedCount, rows.Count);
        for (var i = 0; i < shownCount; i++)
        {
            DrawRow(rows[i], i, rowHeight);
        }

        FormInputs.AddVerticalSpace(BottomPadding);   // breathing room under the last row
    }

    /// <summary>Resets the expansion state when the help topic changes, so a new operator starts collapsed.</summary>
    internal void Reset() => _showAll = false;

    /// <summary>
    /// Pixel height the list will occupy for the operator (0 if it has no rows), so the Help window can
    /// dock it as a fixed footer. Capped at half <paramref name="maxHeight"/> — beyond that the footer scrolls.
    /// </summary>
    internal float MeasureHeight(string? mentionKey, IReadOnlyList<LinkRow> links, float maxHeight)
    {
        var rows = BuildRows(mentionKey, links);
        if (rows.Count == 0)
            return 0;

        ImGui.PushFont(Fonts.FontSmall);
        var smallLine = ImGui.GetTextLineHeightWithSpacing();
        ImGui.PopFont();

        var rowHeight = RowHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var topPadding = 5;
        var rowCount = _showAll ? rows.Count : Math.Min(CollapsedCount, rows.Count);

        var scale = T3Ui.UiScaleFactor;
        var height = ImGui.GetStyle().WindowPadding.Y * 2 // the footer child's own top/bottom padding
                     + 3 * scale + smallLine               // heading (with inline show-all) + the space under it
                     + rowCount * (rowHeight + spacing)
                     + topPadding
                     + BottomPadding * scale;              // breathing room under the last row
        return Math.Min(height, maxHeight * 0.5f);
    }

    /// <summary>Merges hand-authored links and scored video segments into one list, ranked best-first.</summary>
    private List<Row> BuildRows(string? mentionKey, IReadOnlyList<LinkRow> links)
    {
        var rows = new List<Row>(links.Count + 4);

        var referencedVideoIds = new HashSet<string>();
        if (!string.IsNullOrEmpty(mentionKey))
        {
            foreach (var scored in GetRanked(mentionKey))
            {
                rows.Add(Row.ForVideo(scored));
                referencedVideoIds.Add(scored.Segment.VideoId);
            }
        }

        // A hand-authored link that points to a video we already extract is a duplicate — drop it so the
        // (often higher-quality, focus-boosted) extracted reference is the one shown.
        for (var i = 0; i < links.Count; i++)
        {
            if (string.IsNullOrEmpty(links[i].Url))
                continue;
            var youTubeId = ExtractYouTubeId(links[i].Url);
            if (youTubeId != null && referencedVideoIds.Contains(youTubeId))
                continue;
            rows.Add(Row.ForLink(links[i], LinkBaseScore - i * 0.01f));
        }

        rows.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        return rows;
    }

    /// <summary>Pulls the 11-char video id out of a youtu.be/… or …v=… URL, or null if it isn't one.</summary>
    private static string? ExtractYouTubeId(string url)
    {
        foreach (var marker in YouTubeMarkers)
        {
            var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;
            var start = idx + marker.Length;
            var end = start;
            while (end < url.Length && (char.IsLetterOrDigit(url[end]) || url[end] == '_' || url[end] == '-'))
                end++;
            if (end - start >= 10)
                return url.Substring(start, end - start);
        }

        return null;
    }

    /// <summary>Names the section after what's present — specific when the videos are one type, generic otherwise.</summary>
    private static string SectionHeader(IReadOnlyList<Row> rows)
    {
        bool hasTutorial = false, hasMeetup = false, hasUpdate = false, hasOther = false;
        foreach (var row in rows)
        {
            if (row.IsLink)
                continue;
            HelpIndex.TryGetVideo(row.Segment!.VideoId, out var video);
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

    private void DrawRow(Row row, int index, float rowHeight)
    {
        if (row.IsLink)
            DrawLinkRow(row.Link, index, rowHeight);
        else
            DrawVideoRow(row.Segment!, row.PredatesCurrentUi, index, rowHeight);
    }

    private static void DrawLinkRow(LinkRow link, int index, float rowHeight)
    {
        var scale = T3Ui.UiScaleFactor;
        var width = ImGui.GetContentRegionAvail().X;

        ImGui.PushID(1000 + index);
        ImGui.InvisibleButton("##link", new Vector2(width, rowHeight));
        ImGui.PopID();

        var hovered = ImGui.IsItemHovered();
        var clicked = ImGui.IsItemClicked();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();

        if (hovered)
            drawList.AddRectFilled(min, max, UiColors.BackgroundActive.Fade(0.2f), 5 * scale);

        // Links share the video rows' styling so the merged list reads as one consistent list.
        var accentColor = UiColors.StatusActivated.Fade(hovered ? 1 : 0.8f);
        var color = Color.Mix(UiColors.StatusActivated, UiColors.ForegroundFull, 0.3f).Fade(hovered ? 1 : 0.8f);

        var icon = link.Icon ?? Icon.PlayOutput;
        Icons.GetGlyphDefinition(icon, out _, out var iconSize);
        var iconPos = new Vector2(min.X + 4 * scale, min.Y + (rowHeight - iconSize.Y) * 0.5f);
        Icons.DrawIconAtScreenPosition(icon, iconPos, drawList, accentColor);
        var textX = min.X + 4 * scale + iconSize.X + 6 * scale;

        var textY = min.Y + (rowHeight - ImGui.GetTextLineHeight()) * 0.5f;
        ImGui.PushFont(Fonts.FontBold);
        drawList.AddText(new Vector2(textX, textY), color, link.Title);
        ImGui.PopFont();

        if (clicked && !string.IsNullOrEmpty(link.Url))
            CoreUi.Instance.OpenWithDefaultApplication(link.Url);

        if (hovered && !string.IsNullOrEmpty(link.Description))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(260 * scale);
            ImGui.TextUnformatted(link.Description);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }

    private void DrawVideoRow(HelpIndex.OnlineVideoSegment segment, bool predatesCurrentUi, int index, float rowHeight)
    {
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

        if (predatesCurrentUi)
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
        // Prefer the per-reference frame (the exact moment); fall back to the video's cover thumbnail.
        var hasPreview = video != null
                         && (VideoThumbnails.TryGetMoment(video.Id, segment.StartSecond, out textureId, out aspectRatio)
                             || VideoThumbnails.TryGetTexture(video.Id, out textureId, out aspectRatio));

        var thumbnailWidth = 150 * scale;
        var textWidth = 260 * scale;
        var padding = 10 * scale;
        var tooltipWidth = (hasPreview ? thumbnailWidth + 10 * scale : 0) + textWidth + padding * 2;

        ImGui.SetNextWindowSizeConstraints(new Vector2(tooltipWidth, 0), new Vector2(tooltipWidth, ImGui.GetMainViewport().WorkSize.Y));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(padding, 9 * scale));
        ImGui.BeginTooltip();

        if (hasPreview)
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

    private IReadOnlyList<ScoredSegment> GetRanked(string mentionKey)
    {
        if (_rankedCache.TryGetValue(mentionKey, out var cached))
            return cached;

        var segments = HelpIndex.GetSegments(mentionKey);
        var scored = new List<ScoredSegment>(segments.Count);
        var now = DateTime.UtcNow;
        foreach (var segment in segments)
        {
            HelpIndex.TryGetVideo(segment.VideoId, out var video);
            scored.Add(Score(segment, video, now));
        }

        scored.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        _rankedCache[mentionKey] = scored;
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

        // What the clip offers — mirrors the index's purpose weighting (Example/Comparison first).
        var purposeScore = segment.Purpose switch
                               {
                                   "Example"     => 1.2f,
                                   "Comparison"  => 1.2f,
                                   "Parameters"  => 1.1f,
                                   "Gotcha"      => 1.1f,
                                   "Tip"         => 0.9f,
                                   _             => 1.0f,
                               };

        // A focus video is the dedicated tutorial for this op — surface it first, matching the index boost.
        var focusBoost = segment.IsFocus ? 5f : 1f;

        // Operator math is stable, so age decays gently here (the heavier ui:-topic curve is a Phase 2 concern).
        var yearsOld = video?.Date != null ? (float)(now - video.Date.Value).TotalDays / 365.25f : 1f;
        var ageScore = (1f - yearsOld * 0.07f).Clamp(0.45f, 1f);

        var predatesCurrentUi = yearsOld >= PredatesCurrentUiYears;
        var deEmphasized = segment.Style == "experiment" || (segment.Confidence ?? 65) < 50;

        return new ScoredSegment(segment,
                                 depthScore * styleScore * confidenceScore * ageScore * purposeScore * focusBoost,
                                 predatesCurrentUi,
                                 deEmphasized);
    }

    /// <summary>Compact relevancy line for a row, e.g. <c>(5min · In-depth · Example)</c> — duration, depth, purpose.</summary>
    private static string RowAnnotation(HelpIndex.OnlineVideoSegment segment)
    {
        var annotation = "(" + FormatSegmentDuration(segment.DurationSeconds);
        if (!string.IsNullOrEmpty(segment.Depth))
            annotation += " · " + Capitalize(segment.Depth!);
        if (!string.IsNullOrEmpty(segment.Purpose))   // purpose (Example/Gotcha/…) replaces the old style chip
            annotation += " · " + Capitalize(segment.Purpose!);
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

    /// <summary>A unified list entry: either a hand-authored <see cref="LinkRow"/> or a scored video segment.</summary>
    private readonly record struct Row(
        float Score,
        bool IsLink,
        LinkRow Link,
        HelpIndex.OnlineVideoSegment? Segment,
        bool PredatesCurrentUi)
    {
        internal static Row ForLink(LinkRow link, float score) => new(score, true, link, null, false);

        internal static Row ForVideo(ScoredSegment scored) =>
            new(scored.Score, false, default, scored.Segment, scored.PredatesCurrentUi);
    }

    private const int CollapsedCount = 3;
    private const float BottomPadding = 8f;
    // Below a focus tutorial's score (~2.3) but above incidental video mentions — curated links sit just under
    // the dedicated tutorial. (Interim: once symbol links are migrated into the index, this special-casing goes away.)
    private const float LinkBaseScore = 2.0f;
    private static readonly string[] YouTubeMarkers = { "youtu.be/", "v=" };
    private const float PredatesCurrentUiYears = 2f;
    private const int MaxTitleChars = 84;

    private bool _showAll;
    private bool _tooltipDrawn;
    private readonly Dictionary<string, IReadOnlyList<ScoredSegment>> _rankedCache = new();
    private readonly MarkdownView _noteView = new(new MarkdownView.Options());
}
