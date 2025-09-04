using T3.Core.Utils;
using T3.Core.IO;

namespace Lib.numbers.anim.vj;

[Guid("79db48d8-38d3-47ca-9c9b-85dde2fa660d")]
internal sealed class ForwardBeatTaps : Instance<ForwardBeatTaps>
{
    [Output(Guid = "71d05d91-d18b-44b3-a469-392739fd6941")]
    public readonly Slot<Command> Result = new();

    public ForwardBeatTaps()
    {
        Result.UpdateAction += Update;
    }
        
    private void Update(EvaluationContext context)
    {
        var tapProvider = TapProvider.Instance;

        // Handle beat tap trigger
        BeatTapTriggered = MathUtils.WasTriggered(TriggerBeatTap.GetValue(context), ref _wasBeatTriggered);
        tapProvider.BeatTapTriggered = BeatTapTriggered;

        // Handle resync trigger
        ResyncTriggered = MathUtils.WasTriggered(TriggerResync.GetValue(context), ref _wasResyncTriggered);
        tapProvider.ResyncTriggered = ResyncTriggered;

        var offset = SlideSyncTimeOffset.GetValue(context);
        if (!float.IsNaN(offset))
        {
            //Log.Debug($"Set Slide time {offset}", this);
            SlideSyncTime = offset;
            tapProvider.SlideSyncTime = SlideSyncTime;
        }
            
        // Evaluate subtree
        SubTree.GetValue(context);
    }

    private bool _wasBeatTriggered;
    private bool _wasResyncTriggered;


    // These will be process every frame by the editor
    public static bool BeatTapTriggered { get; private set; }
    public static bool ResyncTriggered { get; private set; }
    public static float SlideSyncTime { get; private set; }
        
        
    [Input(Guid = "89576f05-3f3d-48d1-ab63-f3c16c85db63")]
    public readonly InputSlot<Command> SubTree = new();
        
    [Input(Guid = "37DA48AC-A7C5-47C8-9FB3-82D4403B2BA0")]
    public readonly InputSlot<bool> TriggerBeatTap = new();

    [Input(Guid = "58B6DF86-B02E-4183-9B63-1033C9DFF25F")]
    public readonly InputSlot<bool> TriggerResync = new();
        
    [Input(Guid = "2E18AE65-044E-443D-9288-7A9BB6864514")]
    public readonly InputSlot<float> SlideSyncTimeOffset = new();

}