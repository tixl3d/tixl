#nullable enable
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace T3.Editor.Gui.Help;

/// <summary>
/// Loads and caches the documentation cross-reference indices that ship under
/// <c>.help/references/indices/</c> (built by the video-analysis pipeline): the operator/ui → meet-up
/// segment spine (<c>mentions.json</c>) and the video catalog (<c>videos.json</c>). Resolved via
/// <see cref="ShippedContent"/> so a dev checkout reads the live repo files and a release reads the copy
/// placed next to the binaries.
/// </summary>
internal static class HelpIndex
{
    /// <summary>A single meet-up / tutorial segment that discusses a topic, with its relevancy axes.</summary>
    internal sealed record OnlineVideoSegment(
        string VideoId,
        int StartSecond,
        int DurationSeconds,
        string Url,
        string? Depth,
        string? Style,
        string? Purpose,
        int? Confidence,
        string Note,
        bool IsFocus,
        int MomentCount);

    /// <summary>Catalog entry for a referenced video.</summary>
    internal sealed record VideoInfo(
        string Id,
        string Type,
        DateTime? Date,
        string Title,
        string Url,
        int DurationSeconds);

    /// <summary>A UI topic (editor concept/component) from the help index — the target of a <c>[ui:Id]</c> link.</summary>
    internal sealed record TopicInfo(string Id, string Term, string Doc);

    /// <summary>Resolves a <c>ui:&lt;id&gt;</c> key (as written in a <c>[ui:Id]</c> link) to its topic.</summary>
    internal static bool TryGetTopic(string topicKey, out TopicInfo? topic)
    {
        EnsureLoaded();
        return _topicsByKey.TryGetValue(topicKey, out topic);
    }

    /// <summary>Meet-up segments discussing the operator with the given full path (namespace + name), newest data first as authored.</summary>
    internal static IReadOnlyList<OnlineVideoSegment> GetOperatorSegments(string operatorFullPath)
    {
        EnsureLoaded();
        return _segmentsByKey.TryGetValue("op:" + operatorFullPath, out var segments)
                   ? segments
                   : Array.Empty<OnlineVideoSegment>();
    }

    internal static bool TryGetVideo(string id, out VideoInfo? video) => _videosById.TryGetValue(id, out video);

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true; // Set first: a parse failure should not retry the read every frame.

        var directory = ShippedContent.ResolveDirectory(".help", "references", "indices");
        LoadVideos(Path.Combine(directory, "videos.json"));
        LoadMentions(Path.Combine(directory, "mentions.json"));
        LoadTopics(Path.Combine(directory, "topics.json"));
    }

    private static void LoadTopics(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var file = JsonConvert.DeserializeObject<TopicsFileDto>(File.ReadAllText(path));
            if (file?.Topics == null)
                return;

            foreach (var (key, dto) in file.Topics)   // key is already "ui:<id>", matching a [ui:Id] link
            {
                if (!string.IsNullOrEmpty(key))
                    _topicsByKey[key] = new TopicInfo(key, dto.Term ?? key, dto.Doc ?? "");
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Could not read help topic index from {path}: {e.Message}");
        }
    }

    private static void LoadVideos(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var file = JsonConvert.DeserializeObject<VideosFileDto>(File.ReadAllText(path));
            if (file?.Videos == null)
                return;

            foreach (var dto in file.Videos)
            {
                if (string.IsNullOrEmpty(dto.Id))
                    continue;

                _videosById[dto.Id] = new VideoInfo(dto.Id,
                                                    dto.Type ?? "",
                                                    ParseDate(dto.Date),
                                                    dto.Title ?? "",
                                                    dto.Url ?? "",
                                                    ParseDurationSeconds(dto.Duration));
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Could not read help video index from {path}: {e.Message}");
        }
    }

    private static void LoadMentions(string path)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var byKey = JsonConvert.DeserializeObject<Dictionary<string, List<SegmentDto>>>(File.ReadAllText(path));
            if (byKey == null)
                return;

            foreach (var (key, dtoList) in byKey)
            {
                var segments = new List<OnlineVideoSegment>(dtoList.Count);
                foreach (var dto in dtoList)
                {
                    if (string.IsNullOrEmpty(dto.Video))
                        continue;

                    segments.Add(new OnlineVideoSegment(dto.Video,
                                                   dto.StartSecond,
                                                   dto.Duration,
                                                   dto.Url ?? "",
                                                   dto.Depth,
                                                   dto.Style,
                                                   dto.Purpose,
                                                   dto.Confidence,
                                                   dto.Note ?? "",
                                                   dto.Focus,
                                                   dto.MomentCount));
                }

                _segmentsByKey[key] = segments;
            }
        }
        catch (Exception e)
        {
            Log.Debug($"Could not read help mention index from {path}: {e.Message}");
        }
    }

    private static DateTime? ParseDate(string? text)
    {
        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                   ? date
                   : null;
    }

    /// <summary>Parses a <c>"h:mm:ss"</c> / <c>"m:ss"</c> duration label into total seconds.</summary>
    internal static int ParseDurationSeconds(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var seconds = 0;
        foreach (var part in text.Split(':'))
        {
            if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return 0;

            seconds = seconds * 60 + value;
        }

        return seconds;
    }

    private sealed class VideosFileDto
    {
        [JsonProperty("videos")] public List<VideoDto>? Videos;
    }

    private sealed class VideoDto
    {
        [JsonProperty("id")] public string? Id;
        [JsonProperty("type")] public string? Type;
        [JsonProperty("date")] public string? Date;
        [JsonProperty("title")] public string? Title;
        [JsonProperty("url")] public string? Url;
        [JsonProperty("duration")] public string? Duration;
    }

    private sealed class SegmentDto
    {
        [JsonProperty("video")] public string? Video;
        [JsonProperty("startSecond")] public int StartSecond;
        [JsonProperty("duration")] public int Duration;
        [JsonProperty("url")] public string? Url;
        [JsonProperty("depth")] public string? Depth;
        [JsonProperty("style")] public string? Style;
        [JsonProperty("purpose")] public string? Purpose;
        [JsonProperty("confidence")] public int? Confidence;
        [JsonProperty("note")] public string? Note;
        [JsonProperty("focus")] public bool Focus;
        [JsonProperty("momentCount")] public int MomentCount;
    }

    private sealed class TopicsFileDto
    {
        [JsonProperty("topics")] public Dictionary<string, TopicDto>? Topics;
    }

    private sealed class TopicDto
    {
        [JsonProperty("term")] public string? Term;
        [JsonProperty("doc")] public string? Doc;
    }

    private static bool _loaded;
    private static readonly Dictionary<string, List<OnlineVideoSegment>> _segmentsByKey = new();
    private static readonly Dictionary<string, VideoInfo> _videosById = new();
    private static readonly Dictionary<string, TopicInfo> _topicsByKey = new();
}
