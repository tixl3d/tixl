using T3.Core.Animation;

namespace Lib.numbers.anim.time;

[Guid("fcd32cac-6544-42a3-8a14-203b8ca3559e")]
internal sealed class SetPlaybackSpeed : Instance<SetPlaybackSpeed>
{
    [Output(Guid = "80fc0900-074f-43b8-a053-991159b7f56e")]
    public readonly Slot<Command> Commands = new();
        
    public SetPlaybackSpeed()
    {
        Commands.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var speedFactor = SpeedFactor.GetValue(context);
        var triggered = TriggerUpdate.GetValue(context);

        //var wasTriggered = MathUtils.WasTriggered(TriggerUpdate.GetValue(context), ref _triggerUpdate);
            
        if (triggered)
        {
            if (Playback.Current == null)
            {
                Log.Warning("Can't set BPM-Rate without active Playback", this);
                return;
            }

            if (speedFactor > 0.95f && speedFactor < 1.05f)
            {
                speedFactor = 1;
            }
            else if (speedFactor > 0 && speedFactor < 0.03)
            {
                speedFactor = 0.0001f;  // not quite stopping playback 
            }
            Playback.Current.PlaybackSpeed = speedFactor;
        }
            
        SubGraph.GetValue(context);
    }

    //private bool _triggerUpdate;
        
    [Input(Guid = "4fdef030-d1d1-403a-b16c-a414f63fc913")]
    public readonly InputSlot<Command> SubGraph = new();
                
    [Input(Guid = "10f14ef0-7399-4a85-96ad-f445903eec1d")]
    public readonly InputSlot<float> SpeedFactor = new();
        
    [Input(Guid = "f3b73860-e44d-4ae1-9709-51ab142a2d0a")]
    public readonly InputSlot<bool> TriggerUpdate = new();

}