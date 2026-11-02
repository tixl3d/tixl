using T3.Core.Animation;

namespace Lib.render.postfx.@_;

/// <summary>
/// Counts the frames this operator was evaluated in. Unlike [CountInt] it keeps counting while playback is paused,
/// so feedback passes keep swapping their history targets when the camera moves through a paused scene.
/// </summary>
[Guid("d4a81f36-2c97-4e05-b1a6-73f0e9c5d218")]
internal sealed class _CountFrames : Instance<_CountFrames>
{
    [Output(Guid = "e5b92a47-3da8-4f16-82b7-8401fad6e329", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Count = new();

    public _CountFrames()
    {
        Count.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (_lastUpdateFrame == Playback.FrameCount)
            return;

        _lastUpdateFrame = Playback.FrameCount;
        Count.Value++;
    }

    private int _lastUpdateFrame = -1;
}
