using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Utils;

namespace Lib.numbers.anim.vj;

[Guid("f5158500-39e4-481e-aa4f-f7dbe8cbe0fa")]
internal sealed class SetBpm : Instance<SetBpm>
{
    [Output(Guid = "05C17586-CF93-4244-9979-47E310ABAF31")]
    public readonly Slot<Command> Commands = new();
        
    public SetBpm()
    {
        Commands.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var bpm = BpmRate.GetValue(context);

        var wasTriggered = MathUtils.WasTriggered(TriggerUpdate.GetValue(context), ref _triggerUpdate);
            
        var clampedRate = bpm.Clamp(54, 240);
        if (wasTriggered && bpm > 1)
        {
            if (Playback.Current == null)
            {
                Log.Warning("Can't set BPM-Rate without active Playback", this);
            }
            else
            {
                Log.Debug($"Setting BPM rate to {clampedRate}", this);
                //_setBpmTriggered = true;
                _newBpmRate = clampedRate;

                // This will be picked up by PlaybackUtils
                BpmProvider.Instance.NewBpmRate = clampedRate;
                BpmProvider.Instance.SetBpmTriggered = true;
            }
        }
        SubGraph.GetValue(context);
    }

        
    // NOTE: This used to be process every frame by the editor
    // TODO: this is now obsolete and should be removed
    // public static bool TryGetNewBpmRate(out float bpm)
    // {
    //     if (!_setBpmTriggered)
    //     {
    //         bpm = _newBpmRate;
    //         return false;
    //     }
    //
    //     _setBpmTriggered = false;
    //     bpm = _newBpmRate;
    //     return true;
    // }
        
    //private static bool _setBpmTriggered;
    private static float _newBpmRate;
    private bool _triggerUpdate;
        
    [Input(Guid = "9CC32DA8-F939-4AD3-B381-6DF8338A371B")]
    public readonly InputSlot<Command> SubGraph = new();
                
    [Input(Guid = "721C34B5-BB06-49E0-A71E-2AEBBF2557E0")]
    public readonly InputSlot<float> BpmRate = new();
        
    [Input(Guid = "FBF10760-B559-4E9C-B8DC-CE61D3F21C82")]
    public readonly InputSlot<bool> TriggerUpdate = new();

}