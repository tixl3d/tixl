#nullable enable

using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3CoreDataClip = T3.Core.DataTypes.DataSet.DataClip;

namespace Lib.io.data;

/// <summary>
/// Loads a recorded <c>.data</c> file and presents it as a timeline-bound
/// <see cref="T3CoreDataClip"/>. Owns the <see cref="Resource{T}"/> load +
/// <see cref="T3.Core.Animation.TimeClip"/> placement, then publishes the result as a
/// single <see cref="T3CoreDataClip"/> output. Downstream ops consume the clip and read
/// <c>.Set</c> for content and <c>.Mapping</c> for timing.
/// </summary>
/// <remarks>
/// Loading flows through the shared <see cref="DataSetCache"/>, so multiple
/// <c>LoadDataClip</c> ops referencing the same file share one parsed
/// <see cref="DataSet"/>. The <c>Resource&lt;DataSet&gt;</c> wrapper additionally gives
/// file-watch invalidation when the source is re-recorded.
/// </remarks>
[Guid("4d1c0e80-7b2a-4f6d-9c1b-12d3e4f50607")]
internal sealed class LoadDataClip : Instance<LoadDataClip>, IStatusProvider, IDescriptiveFilename
{
    [Output(Guid = "5e2d1f81-8c3b-4a7e-ad2c-23e4f5061718", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly TimeClipSlot<T3CoreDataClip?> Clip = new();

    public LoadDataClip()
    {
        _dataSetResource = new Resource<DataSet>(FilePath, TryLoad);
        _dataSetResource.AddDependentSlots(Clip);
        Clip.UpdateAction += Update;
    }

    private bool TryLoad(FileResource file,
                         DataSet? currentValue,
                         [NotNullWhen(true)] out DataSet? newValue,
                         [NotNullWhen(false)] out string? failureReason)
    {
        // Let the cache decide whether to re-parse. It already keys entries by file path
        // + last-write timestamp, so a real file change forces a fresh parse; otherwise
        // every LoadDataClip pointing at the same path shares one DataSet instance.
        // That sharing matters for in-memory mutations (e.g. "Remove channel" in the
        // DataSet output view): a Split that creates a sibling LoadDataClip used to
        // wipe those edits because we explicitly invalidated here on every TryLoad,
        // re-parsing the on-disk file and resurrecting the removed channels.
        var absolutePath = file.AbsolutePath;
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
        if (dataSet == null)
        {
            Clip.Value = null;
            return;
        }

        // SourceRange is in file-time (bars), TimeRange in timeline-time (bars).
        // Recording sets SourceRange = (0, durationBars) — file events are anchored to
        // record-start (file-time 0). Cut / drag-start trim adjust SourceRange in
        // file-time directly, so the mapping is a straight identity build with no
        // rebasing. Same convention as PlayVideoClip / MidiClip — keeps split, trim,
        // and body-drag math consistent across clip types.
        var timeRange = Clip.TimeClip.TimeRange;
        var sourceRange = Clip.TimeClip.SourceRange;
        var mapping = new TimeRangeMapping(timeRange, sourceRange, context.Playback.Bpm);

        Clip.Value = new T3CoreDataClip
                         {
                             Set = dataSet,
                             Mapping = mapping,
                         };
        Clip.DirtyFlag.Clear();
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
