#nullable enable

using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.IoServices;
using T3CoreDataClip = T3.Core.DataTypes.DataSet.DataClip;

namespace Lib.io.midi;

/// <summary>
/// Loads a standard MIDI file (<c>.mid</c> / <c>.midi</c>) and presents it as a
/// timeline-bound <see cref="T3CoreDataClip"/>, converted to the same channel conventions
/// as live MIDI recording. Owns the <see cref="Resource{T}"/> load +
/// <see cref="T3.Core.Animation.TimeClip"/> placement, then publishes the result as a
/// single <see cref="T3CoreDataClip"/> output — downstream ops (e.g. <c>SimulateIoData</c>)
/// consume it exactly like a recorded <c>.data</c> clip.
/// </summary>
/// <remarks>
/// Parsing flows through the shared <see cref="MidiFileToDataSet"/> cache, so multiple
/// <c>MidiClip</c> ops referencing the same file share one converted
/// <see cref="DataSet"/>. The <c>Resource&lt;DataSet&gt;</c> wrapper additionally gives
/// file-watch invalidation when the source file changes.
/// </remarks>
[Guid("b4766419-8bca-4fa0-a398-e6af90ef8971")]
internal sealed class MidiClip : Instance<MidiClip>, IStatusProvider, IDescriptiveFilename
{
    [Output(Guid = "a668ec72-17a6-443d-9a85-04d9c37ec9d3", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly TimeClipSlot<T3CoreDataClip?> Clip = new();

    public MidiClip()
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
        // The converter cache keys by (path, last-write timestamp), so a changed file
        // re-converts while unchanged files are shared across all MidiClip instances.
        if (!MidiFileToDataSet.TryGet(file.AbsolutePath, out var loaded, out var reason))
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

        // SourceRange is in file-time (bars), TimeRange in timeline-time (bars) — the same
        // identity-mapping convention as LoadDataClip / PlayVideoClip, so split, trim and
        // body-drag math stay consistent across clip types.
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
    private static readonly string[] _fileFilter = ["*.mid", "*.midi"];

    [Input(Guid = "10c6978a-5910-4949-8382-95834dd1b4d1")]
    public readonly InputSlot<string> FilePath = new();

    private readonly Resource<DataSet> _dataSetResource;
    private string _errorMessageForStatus = string.Empty;
}
