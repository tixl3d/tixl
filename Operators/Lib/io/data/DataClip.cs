#nullable enable

using T3.Core.DataTypes.DataSet;

namespace Lib.io.data;

/// <summary>
/// Plays a recorded <c>.data</c> file as a timeline-bound clip. The operator appears in
/// the timeline clip area and exposes the parsed <see cref="DataSet"/> as a graph
/// output, plus the current playhead position mapped into the source's local time so
/// downstream consumers (e.g. <c>SimulateIoData</c>, Phase 3c) can slice events.
/// </summary>
/// <remarks>
/// <para>
/// Loading goes through the standard <see cref="Resource{T}"/> machinery so file
/// changes (re-recording over the same file, an external editor saving the JSON) are
/// picked up automatically. Parsing itself flows through the shared
/// <see cref="DataSetCache"/> — multiple <c>DataClip</c> ops referencing the same file
/// share one parsed <see cref="DataSet"/> instance.
/// </para>
/// <para>
/// Time mapping mirrors <c>AudioClip</c>: playhead-local bars are translated through
/// <see cref="T3.Core.Animation.TimeClip.TimeRange"/> / <see cref="T3.Core.Animation.TimeClip.SourceRange"/>
/// and finally converted to seconds via the active <see cref="Playback"/>'s BPM. The
/// dataset's events use seconds-relative-to-record-start as their time base (see
/// <see cref="T3.Core.IO.DataSetSessionRecorder"/>), so the seconds output here is in the
/// same units.
/// </para>
/// <para>
/// New in Phase 3b of the live-session recording feature
/// (see <c>.agentic/Plans/Plan_LiveSessionRecording.md</c>).
/// </para>
/// </remarks>
[Guid("4d1c0e80-7b2a-4f6d-9c1b-12d3e4f50607")]
internal sealed class DataClip : Instance<DataClip>, IStatusProvider, IDescriptiveFilename
{
    [Output(Guid = "5e2d1f81-8c3b-4a7e-ad2c-23e4f5061718", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly TimeClipSlot<DataSet?> Set = new();

    [Output(Guid = "6f3e2092-9d4c-4b8f-be3d-34f506172829")]
    public readonly Slot<float> LocalSourceTimeSecs = new();

    public DataClip()
    {
        _dataSetResource = new Resource<DataSet>(FilePath, TryLoad);
        _dataSetResource.AddDependentSlots(Set);
        _dataSetResource.AddDependentSlots(LocalSourceTimeSecs);
        Set.UpdateAction += Update;
        LocalSourceTimeSecs.UpdateAction += Update;
    }

    private bool TryLoad(FileResource file,
                         DataSet? currentValue,
                         [NotNullWhen(true)] out DataSet? newValue,
                         [NotNullWhen(false)] out string? failureReason)
    {
        // Re-parse on every TryLoad invocation. The Resource<> file-watch invalidates the
        // lazy value when the file changes, which is when this gets called — but the shared
        // DataSetCache is the source of truth for "have we already parsed this version?",
        // so we explicitly invalidate before re-asking. That way all DataClip ops pointing
        // at the same file see the new content in lockstep.
        var absolutePath = file.AbsolutePath;
        DataSetCache.Invalidate(absolutePath);

        if (!DataSetCache.TryGet(absolutePath, out var loaded, out var reason))
        {
            newValue = null;
            failureReason = reason;
            _errorMessageForStatus = reason;
            return false;
        }

        newValue = loaded;
        failureReason = null;
        _errorMessageForStatus = string.Empty;
        return true;
    }

    private void Update(EvaluationContext context)
    {
        var dataSet = _dataSetResource.GetValue(context);

        // Map local playhead time (bars) → source time (bars) → seconds, mirroring AudioClip.
        // TimeRange.End == TimeRange.Start is the sentinel for "no explicit duration"; in
        // that case skip the rate scaling so the source plays at native rate from clip start.
        var timeRange = Set.TimeClip.TimeRange;
        var sourceRange = Set.TimeClip.SourceRange;
        var posInSourceBars = context.LocalTime - timeRange.Start;
        if (Math.Abs(timeRange.End - timeRange.Start) > 0.0001f)
        {
            var rate = (sourceRange.End - sourceRange.Start) / (timeRange.End - timeRange.Start);
            posInSourceBars *= rate;
        }
        posInSourceBars += sourceRange.Start;

        var sourceTimeInSecs = context.Playback.SecondsFromBars(posInSourceBars);

        Set.Value = dataSet;
        LocalSourceTimeSecs.Value = (float)sourceTimeInSecs;
        Set.DirtyFlag.Clear();
        LocalSourceTimeSecs.DirtyFlag.Clear();
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
        => string.IsNullOrEmpty(_errorMessageForStatus)
               ? IStatusProvider.StatusLevel.Success
               : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _errorMessageForStatus;

    public IEnumerable<string> FileFilter => _fileFilter;
    public InputSlot<string> SourcePathSlot => FilePath;
    private static readonly string[] _fileFilter = ["*.data"];

    [Input(Guid = "70419103-ae5d-4ca0-cf4e-456071829304")]
    public readonly InputSlot<string> FilePath = new();

    private readonly Resource<DataSet> _dataSetResource;
    private string _errorMessageForStatus = string.Empty;
}
