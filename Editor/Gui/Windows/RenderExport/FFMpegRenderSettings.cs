#nullable enable
namespace T3.Editor.Gui.Windows.RenderExport;

using FFMpegCore.Enums;

internal sealed class FFMpegRenderSettings
{
    public static readonly FFMpegRenderSettings Current = new()
    {
        Reference = TimeReference.Bars,
        StartInBars = 0f,
        EndInBars = 4f,
        Fps = 60f,
        OverrideMotionBlurSamples = -1,
    };

    public TimeReference Reference;
    public float StartInBars;
    public float EndInBars;
    public float Fps;
    public int OverrideMotionBlurSamples;


    public RenderSettings.RenderModes RenderMode = RenderSettings.RenderModes.Video;
    public int Bitrate = 25_000_000;
    public bool ExportAudio = true;
    
    public bool AutoIncrementVideo = true;
    public ImageFileFormats FileFormat = ImageFileFormats.WebP;
    public bool CreateSubFolder = true;
    public bool AutoIncrementSubFolder = true;
    
    public bool IsAutoIncrementing => RenderMode switch
                                      {
                                          RenderSettings.RenderModes.Video         => AutoIncrementVideo,
                                          RenderSettings.RenderModes.ImageSequence => AutoIncrementSubFolder,
                                          _                                        => false
                                      };

    public TimeRanges TimeRange = TimeRanges.Custom;
    public float ResolutionFactor = 1f;

    public int FrameCount;
    public int WebpQuality = 75;
    public int WebpCompressionLevel = 0;

    
    public enum ImageFileFormats
    {
        Png,
        Jpg,
        WebP,
    }

    public bool ShowAdvancedSettings;
    public SelectedCodec Codec = SelectedCodec.OpenH264;
    public int CrfQuality = 23;
    public Speed Preset = Speed.Fast;

    internal enum SelectedCodec
    {
        OpenH264,
        ProRes,
        Hap,
        HapAlpha,
        Vp9,
    }

    public static string GetFileExtension(SelectedCodec codec)
    {
        return codec switch
        {
            SelectedCodec.OpenH264 => ".mp4",
            SelectedCodec.ProRes => ".mov",
            SelectedCodec.Hap => ".mov",
            SelectedCodec.HapAlpha => ".mov",
            SelectedCodec.Vp9 => ".webm",
            _ => ".mp4"
        };
    }

    internal enum TimeReference
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
