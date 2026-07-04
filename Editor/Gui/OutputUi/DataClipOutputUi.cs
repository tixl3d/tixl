using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Resource.Assets;
using T3.IoServices;

namespace T3.Editor.Gui.OutputUi;

/// <summary>
/// Output-side preview for <see cref="DataClip"/> slots — unwraps the clip's underlying
/// <see cref="DataSet"/> and renders it via the same canvas used by
/// <see cref="DataSetOutputUi"/>. Lets the user inspect a DataClip on the graph the
/// same way they inspect a raw DataSet, without needing to manually pluck <c>.Set</c>
/// out into a separate debug op.
/// </summary>
internal sealed class DataClipOutputUi : OutputUi<DataClip>
{
    public override IOutputUi Clone()
    {
        return new DataClipOutputUi()
                   {
                       OutputDefinition = OutputDefinition,
                       PosOnCanvas = PosOnCanvas,
                       Size = Size,
                   };
    }

    protected override void DrawTypedValue(ISlot slot, string viewId)
    {
        if (slot is not Slot<DataClip> clipSlot)
            return;

        // Preferred path: the slot's been evaluated this frame by a downstream consumer,
        // so we get the freshly-built DataSet *and* TimeRangeMapping for free.
        if (clipSlot.Value is { Set: not null } liveClip)
        {
            _canvas.Draw(liveClip);
            return;
        }

        // Fallback when nothing pulls the slot. TimeClipSlot wraps its UpdateAction in
        // UpdateWithTimeRangeCheck, which skips the inner action when the playhead sits
        // outside the clip's TimeRange — so LoadDataClip.Update never runs in that case
        // and slot.Value stays null. Force-Recomputing here would just hit the same
        // gate. Instead we resolve the DataSet ourselves through the shared cache and
        // build the mapping directly from the live TimeClip, matching exactly what
        // LoadDataClip.Update would produce. Preview ends up working stand-alone even
        // before anything's connected.
        var fallbackClip = TryBuildFallbackClip(clipSlot);
        if (fallbackClip != null)
            _canvas.Draw(fallbackClip);
    }

    private static DataClip? TryBuildFallbackClip(Slot<DataClip> clipSlot)
    {
        var instance = clipSlot.Parent;
        if (instance is not IDescriptiveFilename descriptive)
            return null;

        var path = descriptive.SourcePathSlot.TypedInputValue.Value;
        if (string.IsNullOrEmpty(path))
            return null;

        if (!AssetRegistry.TryResolveAddress(path, instance, out var absolutePath, out _))
            return null;

        if (!DataClipFiles.TryGetDataSetForFile(path, absolutePath, out var dataSet, out _))
            return null;

        // Mapping is meaningful only when the slot is a TimeClipSlot — every concrete
        // DataClip-producer today is one (LoadDataClip). For other shapes we still surface
        // the DataSet, just without the clip-mode playhead / boundary overlay.
        TimeRangeMapping? mapping = null;
        if (clipSlot is TimeClipSlot<DataClip> timeClipSlot && Playback.Current is { } playback)
        {
            mapping = new TimeRangeMapping(timeClipSlot.TimeClip.TimeRange,
                                            timeClipSlot.TimeClip.SourceRange,
                                            playback.Bpm);
        }

        return new DataClip
                   {
                       Set = dataSet,
                       Mapping = mapping,
                   };
    }

    private readonly DataSetViewCanvas _canvas = new();
}
