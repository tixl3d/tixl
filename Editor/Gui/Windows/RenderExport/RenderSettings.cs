#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

internal sealed class RenderSettings
{
    public static readonly RenderSettings Defaults = new();

    /// <summary>
    /// Returns the render settings for the focused composition, initializing from defaults
    /// (with legacy path migration) if none exist yet.
    /// </summary>
    public static RenderSettings Current
    {
        get
        {
            var symbolUi = ProjectView.Focused?.CompositionInstance?.Symbol.GetSymbolUi();
            if (symbolUi == null)
                return Defaults;

            if (symbolUi.RenderSettings != null)
                return symbolUi.RenderSettings;

            // First access for this composition — initialize with defaults + legacy paths
            var settings = new RenderSettings();

            #pragma warning disable CS0612 // Obsolete
            var legacy = UserSettings.Config;
            if (!string.IsNullOrEmpty(legacy.RenderVideoFilePath))
                settings.VideoFilePath = legacy.RenderVideoFilePath;
            if (!string.IsNullOrEmpty(legacy.RenderSequenceFilePath))
                settings.SequenceFilePath = legacy.RenderSequenceFilePath;
            if (!string.IsNullOrEmpty(legacy.RenderSequenceFileName))
                settings.SequenceFileName = legacy.RenderSequenceFileName;
            if (!string.IsNullOrEmpty(legacy.RenderSequencePrefix))
                settings.SequencePrefix = legacy.RenderSequencePrefix;
            #pragma warning restore CS0612

            symbolUi.RenderSettings = settings;
            symbolUi.FlagAsModified();
            return settings;
        }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public TimeReferences TimeReference;
    public float StartInBars;
    public float EndInBars = 4f;
    public float FrameRate = 60f;
    public int OverrideMotionBlurSamples = -1;

    [JsonConverter(typeof(StringEnumConverter))]
    public RenderModes RenderMode = RenderModes.Video;
    public int Bitrate = 25_000_000;
    public bool AutoIncrementVersionNumber = true;
    public bool CreateSubFolder = true;
    public bool AutoIncrementSubFolder = true;
    public bool ExportAudio = true;
    [JsonConverter(typeof(StringEnumConverter))]
    public ScreenshotWriter.FileFormats FileFormat;
    [JsonConverter(typeof(StringEnumConverter))]
    public TimeRanges TimeRange = TimeRanges.Custom;
    public float ResolutionFactor = 1f;

    // Render paths — persisted per-project in .t3ui
    public string VideoFilePath = "./Render/render-v01.mp4";
    public string SequenceFilePath = "./ImageSequence/";
    public string SequenceFileName = "v01";
    public string SequencePrefix = "render";

    public void CopyFrom(RenderSettings other)
    {
        TimeReference = other.TimeReference;
        StartInBars = other.StartInBars;
        EndInBars = other.EndInBars;
        FrameRate = other.FrameRate;
        OverrideMotionBlurSamples = other.OverrideMotionBlurSamples;
        RenderMode = other.RenderMode;
        Bitrate = other.Bitrate;
        AutoIncrementVersionNumber = other.AutoIncrementVersionNumber;
        CreateSubFolder = other.CreateSubFolder;
        AutoIncrementSubFolder = other.AutoIncrementSubFolder;
        ExportAudio = other.ExportAudio;
        FileFormat = other.FileFormat;
        TimeRange = other.TimeRange;
        ResolutionFactor = other.ResolutionFactor;
        VideoFilePath = other.VideoFilePath;
        SequenceFilePath = other.SequenceFilePath;
        SequenceFileName = other.SequenceFileName;
        SequencePrefix = other.SequencePrefix;
    }

    public RenderSettings Clone()
    {
        var clone = new RenderSettings();
        clone.CopyFrom(this);
        return clone;
    }

    #region Serialization

    internal void WriteToJson(JsonTextWriter writer)
    {
        writer.WritePropertyName("RenderExport");
        writer.WriteRawValue(JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    internal static RenderSettings? ReadFromJson(JToken parentToken)
    {
        var token = parentToken["RenderExport"];
        if (token == null)
            return null;

        return token.ToObject<RenderSettings>();
    }

    #endregion

    internal enum RenderModes
    {
        Video,
        ImageSequence
    }

    internal enum TimeReferences
    {
        Bars,
        Seconds,
        Frames
    }

    internal enum TimeRanges
    {
        Custom,
        Loop,
        Soundtrack,
    }

    internal readonly struct QualityLevel
    {
        internal QualityLevel(double bits, string title, string description)
        {
            MinBitsPerPixelSecond = bits;
            Title = title;
            Description = description;
        }

        internal readonly double MinBitsPerPixelSecond;
        internal readonly string Title;
        internal readonly string Description;
    }
}
