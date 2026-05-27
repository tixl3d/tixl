#nullable enable

using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3CoreDataClip = T3.Core.DataTypes.DataSet.DataClip;

namespace Lib.io.data;

/// <summary>
/// Loads a recorded <c>.data</c> file and presents it as a timeline-bound
/// <see cref="T3CoreDataClip"/>. The op is the file → clip adapter: it owns the
/// <see cref="Resource{T}"/> load + <see cref="T3.Core.Animation.TimeClip"/>
/// placement, then publishes the result as a single <see cref="T3CoreDataClip"/>
/// output. Downstream ops (<c>SimulateIoData</c>, future analysis ops) consume
/// the clip and read <c>.Set</c> for content and <c>.Mapping</c> for timing.
/// </summary>
/// <remarks>
/// <para>
/// Loading flows through the shared <see cref="DataSetCache"/>, so multiple
/// <c>LoadDataClip</c> ops referencing the same file share one parsed
/// <see cref="DataSet"/>. The <c>Resource&lt;DataSet&gt;</c> wrapper additionally
/// gives file-watch invalidation when the source is re-recorded.
/// </para>
/// <para>
/// Renamed from <c>DataClip</c> (the op) so the value type
/// <see cref="T3CoreDataClip"/> can own that name unambiguously.
/// </para>
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
        // Re-parse on every TryLoad invocation. The Resource<> file-watch invalidates the
        // lazy value when the file changes; the shared DataSetCache is the source of
        // truth for "have we already parsed this version?", so we explicitly invalidate
        // before re-asking. That way all LoadDataClip ops pointing at the same file see
        // the new content in lockstep.
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
        if (dataSet == null)
        {
            Clip.Value = null;
            return;
        }

        // Build the TimeRangeMapping so that at the clip's left edge the consumer sees
        // source time = 0. The default TiXL TimeClip mirrors SourceRange to TimeRange
        // (i.e. "source is timeline-positioned"), which is the right convention for
        // audio / video where the file plays in place. For .data files, events are
        // stored relative to record-start, so the source content is conceptually
        // "anchored at 0" and the SourceRange's offset *from TimeRange.Start* is the
        // user-controlled trim.
        //
        // Rebase: shift SourceRange so SourceRange.Start - TimeRange.Start becomes the
        // trim offset (in bars). Default state (mirrored) → 0 offset; user shifting
        // SourceRange.Start later → trim into the recording.
        var timeRange = Clip.TimeClip.TimeRange;
        var sourceRange = Clip.TimeClip.SourceRange;
        var trimOffsetBars = sourceRange.Start - timeRange.Start;
        var sourceDurationBars = sourceRange.End - sourceRange.Start;
        var rebasedSourceRange = new TimeRange(trimOffsetBars, trimOffsetBars + sourceDurationBars);

        var mapping = new TimeRangeMapping(timeRange, rebasedSourceRange, context.Playback.Bpm);

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
