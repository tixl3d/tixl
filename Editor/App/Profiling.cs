#nullable enable
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Editor.Gui.UiHelpers;

namespace T3.Editor.App;

internal static class Profiling
{
    internal static void KeepFrameData()
    {
        if (UserSettings.Config.EnableFrameProfiling)
        {
            _frameStartTime = Playback.RunTimeInSecs;
            DebugDataRecording.StartRegion("__Stats/_ProcessFrame", null, ref _frameRegionChannel);
        }

        if (UserSettings.Config.EnableGCProfiling)
        {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            
            TraceGCLevel(0, ref gcMemoryInfo);
            TraceGCLevel(1, ref gcMemoryInfo);
            TraceGCLevel(2, ref gcMemoryInfo);
        }
    }

    internal static void EndFrameData()
    {
        if (!UserSettings.Config.EnableFrameProfiling || _frameRegionChannel == null)
            return;
        
        var frameEndTime = Playback.RunTimeInSecs;
        var duration = frameEndTime - _frameStartTime;
        DebugDataRecording.EndRegion(_frameRegionChannel, $"{duration * 1000:0ms}");
    }

    private static void TraceGCLevel(int level, ref  GCMemoryInfo gcMemoryInfo)
    {
        var heapSizeBytes = gcMemoryInfo.GenerationInfo[level].SizeAfterBytes;
        var lastHeapSizeBytes = _lastHeapSizeBytes[level];
        if (heapSizeBytes == lastHeapSizeBytes)
            return;
        
        DebugDataRecording.KeepTraceData(_gcHeapSizePaths[level], heapSizeBytes, ref _gcHeapSizeChannels[level]);
        DebugDataRecording.KeepTraceData(_gcHeapSizeDeltaPaths[level], heapSizeBytes - lastHeapSizeBytes, ref _gcHeapSizeDeltaChannels[level]);
        _lastHeapSizeBytes[level] = heapSizeBytes;
    }
    
    
    private static readonly string[] _gcHeapSizePaths =
        {
            "__Stats/GC0-HeapSize",
            "__Stats/GC1-HeapSize",
            "__Stats/GC2-HeapSize",
            "__Stats/GC3-HeapSize"
        };

    private static readonly string[] _gcHeapSizeDeltaPaths =
        {
            "__Stats/GC0-HeapSizeDelta",
            "__Stats/GC1-HeapSizeDelta",
            "__Stats/GC2-HeapSizeDelta",
            "__Stats/GC3-HeapSizeDelta"
        };

    private const int MaxGCLevels = 4;
    private static readonly DataChannel[] _gcHeapSizeChannels = new DataChannel[MaxGCLevels];
    private static readonly DataChannel[] _gcHeapSizeDeltaChannels = new DataChannel[MaxGCLevels];
    private static readonly long[] _lastHeapSizeBytes = new long[MaxGCLevels];
    
    private static DataChannel? _frameRegionChannel; // Channels will be created on first use
    private static double _frameStartTime;
    
    public sealed class ProfilingLogWriterClass: ILogWriter
    {
        public void ProcessEntry(ILogEntry entry)
        {
            DebugDataRecording.KeepTraceData("__Stats/_Log", $"{entry.Level}: {entry.Message}", ref _logMessageChannel);
        }

        // TODO: Ths can probably be removed
        public ILogEntry.EntryLevel Filter { get; set; }
        public void Dispose() { }
        private DataChannel? _logMessageChannel;
    }
}