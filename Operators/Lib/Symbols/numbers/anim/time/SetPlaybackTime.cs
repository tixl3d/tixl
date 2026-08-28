using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.anim.time;

[Guid("c6d22dc3-a6ff-4a6f-aa14-8be6595da2b1")]
internal sealed class SetPlaybackTime : Instance<SetPlaybackTime>
{
    [Output(Guid = "0bd5fcb8-72fa-40d5-a922-63a2e7551a88")]
    public readonly Slot<Command> Commands = new();
        
    public SetPlaybackTime()
    {
        Commands.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var newTime = TimeInBars.GetValue(context);
        var mode = TriggerMode.GetEnumValue<Modes>(context);

        var enabled = Enabled.GetValue(context);
        var wasTriggered = MathUtils.WasTriggered(enabled, ref _wasEnabled);
            
        if (float.IsNaN(newTime) || float.IsInfinity(newTime))
        {
            newTime = 0;
        }

        if (wasTriggered || enabled && mode == Modes.Continuously)
        {
            if (Playback.Current == null)
            {
                Log.Warning("Can't set playback time without active Playback", this);
                return;
            }

            // if (Math.Abs(Playback.Current.PlaybackSpeed) < 0.001f)
            // {
            //     Log.Warning("Setting playback speed requires playback to be running.", this);
            // }

            if (ShowLogMessages.GetValue(context))
            {
                Log.Debug($"Setting playback time to {newTime}", this);
            }                
            Playback.Current.TimeInBars = newTime;
        }
            
        SubGraph.GetValue(context);
    }

    private enum Modes
    {
        OnceEnabledGetsTrue,
        Continuously,
    }

    private bool _wasEnabled;
        
    [Input(Guid = "68c3bd6d-aebf-4a0c-bb73-04c9997ebae9")]
    public readonly InputSlot<Command> SubGraph = new();
        
    [Input(Guid = "24cf475d-a5b7-46b5-92cf-1ffcefb0693e")]
    public readonly InputSlot<float> TimeInBars = new();
        
    [Input(Guid = "61AD3F49-8188-45BD-8D72-BBB35C04CA3C", MappedType = typeof(Modes))]
    public readonly InputSlot<int> TriggerMode = new();
        
    [Input(Guid = "BD88E545-743E-4CAA-8FD1-E1F7F3C78B21")]
    public readonly InputSlot<bool> Enabled = new();
        
    [Input(Guid = "16B2B1F5-4087-4E77-902B-3CDDE1B22179")]
    public readonly InputSlot<bool> ShowLogMessages = new();
}