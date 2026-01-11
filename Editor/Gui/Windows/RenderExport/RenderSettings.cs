#nullable enable
namespace T3.Editor.Gui.Windows.RenderExport;

internal sealed class RenderSettings
{
    
    public static readonly RenderSettings Current = new()
                                                        {
                                                            Reference = RenderSettings.TimeReference.Bars,
                                                            StartInBars = 0f,
                                                            EndInBars = 4f,
                                                            Fps = 60f,
                                                            OverrideMotionBlurSamples = -1,
                                                        };
    
    public TimeReference Reference;
    public float StartInBars;
    public float EndInBars;
    public float Fps;
    public int OverrideMotionBlurSamples;   // forwarded for operators that might read it

    
    public  RenderSettings.RenderModes RenderMode = RenderSettings.RenderModes.Video;
    public  int Bitrate = 25_000_000;
    public  bool AutoIncrementVersionNumber = true;
    public  bool CreateSubFolder = false;
    public  bool AutoIncrementSubFolder = true;
    public  bool ExportAudio = true;
    public  ScreenshotWriter.FileFormats FileFormat;
    public  RenderSettings.TimeRanges TimeRange = RenderSettings.TimeRanges.Custom;
    public  float ResolutionFactor = 1f; 

    public int FrameCount;
    
    internal enum RenderModes
    {
        Video,
        ImageSequence
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