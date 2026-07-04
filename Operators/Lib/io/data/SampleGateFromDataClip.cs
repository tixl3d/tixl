#nullable enable

using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3CoreDataClip = T3.Core.DataTypes.DataSet.DataClip;

namespace Lib.io.data;

/// <summary>
/// Samples an interval channel of a <see cref="T3CoreDataClip"/> at a point in time: whether
/// an interval (typically a MIDI note) is active, and its value (the note's velocity).
/// Sibling of <c>SampleFloatFromDataClip</c>, which reads tick channels like CC curves.
/// </summary>
[Guid("b1bce1f1-dab9-4b0f-bd2c-b7f564858675")]
internal sealed class SampleGateFromDataClip : Instance<SampleGateFromDataClip>, IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "609ec999-908c-424e-a3cd-6f3527f21e83", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> Gate = new();

    [Output(Guid = "d242869b-4040-4843-aa78-6baadf103bb2", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Velocity = new();

    [Output(Guid = "dfc8a190-259d-450e-9903-04fcd3d31975", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasHit = new();

    public SampleGateFromDataClip()
    {
        Gate.UpdateAction += Update;
        Velocity.UpdateAction += Update;
        WasHit.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var clip = Clip.GetValue(context);
        var channelPath = Channel.GetValue(context);

        if (!DataClipSampling.TryResolveChannel(clip, channelPath, ref _resolvedChannel, ref _resolvedSet, ref _resolvedPath,
                                                out _errorMessageForStatus))
        {
            Gate.Value = false;
            Velocity.Value = 0;
            WasHit.Value = false;
            return;
        }

        if (_resolvedChannel!.DurationType != ChannelDurationTypes.Interval)
        {
            _errorMessageForStatus = $"Channel '{channelPath}' has no intervals — pick a note channel (N…) or use [SampleFloatFromDataClip].";
            Gate.Value = false;
            Velocity.Value = 0;
            WasHit.Value = false;
            return;
        }

        var time = DataClipSampling.GetSampleTime(context, clip!,
                                                  UseTimeOverride.GetValue(context),
                                                  OverrideTime.GetValue(context));

        Gate.Value = DataClipSampling.TrySampleActiveInterval(_resolvedChannel, time, out var velocity);
        Velocity.Value = velocity;

        // Fires on note starts even when the whole note falls between two frames or a
        // retrigger keeps Gate continuously true.
        WasHit.Value = _hitDetector.Update(Playback.RunTimeInSecs, time, _resolvedChannel);
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

    [Input(Guid = "8f1216bf-c3a6-426c-a861-9038cd2cc448")]
    public readonly InputSlot<T3CoreDataClip?> Clip = new();

    [Input(Guid = "96c0b232-6bdb-4bc2-bfc9-12e53db38c31")]
    public readonly InputSlot<string> Channel = new();

    [Input(Guid = "4289f47e-3f84-4e30-8663-744605374175")]
    public readonly InputSlot<float> OverrideTime = new();

    [Input(Guid = "73a53ce1-4e68-41fb-a4d0-e9fc2aac3f20")]
    public readonly InputSlot<bool> UseTimeOverride = new();

    private DataChannel? _resolvedChannel;
    private DataSet? _resolvedSet;
    private string? _resolvedPath;
    private DataClipSampling.HitDetector _hitDetector;
    private string _errorMessageForStatus = string.Empty;
}
