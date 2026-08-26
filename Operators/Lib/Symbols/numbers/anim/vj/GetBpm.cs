using T3.Core.Animation;

namespace Lib.numbers.anim.vj;

[Guid("6ae8ebb8-3174-463d-9ffb-e14e12eb3029")]
internal sealed class GetBpm : Instance<GetBpm>
{
    [Output(Guid = "551EBFF2-2044-4F28-A6BA-2384A74C8919", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();
        
    public GetBpm()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {

        if (Playback.Current == null)
        {
            Log.Warning("Can't get BPM rate without value playback", this);
            return;
        }

        Result.Value = (float)Playback.Current.Bpm;
    }
}