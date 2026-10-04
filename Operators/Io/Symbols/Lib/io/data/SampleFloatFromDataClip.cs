#nullable enable

using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3CoreDataClip = T3.Core.DataTypes.DataSet.DataClip;

namespace Lib.io.data;

/// <summary>
/// Samples one channel of a <see cref="T3CoreDataClip"/> at a point in time: the value of
/// the last event at or before the sample time. Complements <c>SimulateIoData</c> — instead
/// of replaying events through the simulated IO bus, this reads channel values directly,
/// which suits driving parameters from CC curves or recorded OSC without a MidiInput setup.
/// </summary>
/// <remarks>
/// The sample time defaults to the playhead mapped through the clip's
/// <see cref="T3.Core.Animation.TimeRangeMapping"/> (source seconds, the unit events are
/// stored in). <c>OverrideTime</c> allows procedural access at any source time.
/// The per-frame path is allocation-free: channel resolution is cached and invalidated by
/// reference / string comparison; sampling is a binary search on the event list.
/// </remarks>
[Guid("c0d57acb-29cc-489b-89b0-f016968f2f4c")]
internal sealed class SampleFloatFromDataClip : Instance<SampleFloatFromDataClip>, IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "f29fc9be-8689-4894-a3e1-6b60fdbdae55", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    [Output(Guid = "25469a27-04e8-49bb-b685-241d9de90451", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasHit = new();

    public SampleFloatFromDataClip()
    {
        Result.UpdateAction += Update;
        WasHit.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var clip = Clip.GetValue(context);
        var channelPath = Channel.GetValue(context);
        var fallback = DefaultValue.GetValue(context);

        if (!DataClipSampling.TryResolveChannel(clip, channelPath, ref _resolvedChannel, ref _resolvedSet, ref _resolvedPath,
                                                out _errorMessageForStatus))
        {
            Result.Value = fallback;
            WasHit.Value = false;
            return;
        }

        var time = DataClipSampling.GetSampleTime(context, clip!,
                                                  UseTimeOverride.GetValue(context),
                                                  OverrideTime.GetValue(context));

        if (DataClipSampling.TrySampleLastValueAtTime(_resolvedChannel!, time, out var value))
        {
            Result.Value = value;
        }
        else
        {
            Result.Value = fallback;
        }

        WasHit.Value = _hitDetector.Update(Playback.RunTimeInSecs, time, _resolvedChannel!);
    }

    #region channel dropdown
    string ICustomDropdownHolder.GetValueForInput(Guid inputId) => Channel.Value ?? string.Empty;

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
        => DataClipSampling.GetChannelOptions(inputId, Channel.Id, Clip.Value);

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        if (selected != null)
            Channel.SetTypedInputValue(selected);
    }
    #endregion

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel()
        => string.IsNullOrEmpty(_errorMessageForStatus)
               ? IStatusProvider.StatusLevel.Success
               : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _errorMessageForStatus;

    [Input(Guid = "b69fbaf8-57f7-4b37-b23f-50e1aa0cd4d5")]
    public readonly InputSlot<T3CoreDataClip?> Clip = new();

    [Input(Guid = "237ae57a-2aba-4a6f-9ed0-049635a1e04c")]
    public readonly InputSlot<string> Channel = new();

    [Input(Guid = "10066199-4e20-4f04-af17-5afe359de431")]
    public readonly InputSlot<float> DefaultValue = new();

    [Input(Guid = "61b5ef56-4a32-4026-a0c0-0b0c7b8ee1b1")]
    public readonly InputSlot<float> OverrideTime = new();

    [Input(Guid = "5c470e20-fe8e-461e-9333-05e82ff348f0")]
    public readonly InputSlot<bool> UseTimeOverride = new();

    private DataChannel? _resolvedChannel;
    private DataSet? _resolvedSet;
    private string? _resolvedPath;
    private DataClipSampling.HitDetector _hitDetector;
    private string _errorMessageForStatus = string.Empty;
}
