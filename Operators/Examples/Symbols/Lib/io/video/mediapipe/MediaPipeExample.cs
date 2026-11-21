using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace Examples.Lib.io.video.mediapipe;

/// <summary>
/// All seven MediaPipe operators sharing one camera - PickTexture's Index input flips
/// between their debug views.
/// </summary>
[Guid("9ea50c34-2361-4201-a421-43c9fc1999e4")]
internal sealed class MediaPipeExample : Instance<MediaPipeExample>
{
    [Output(Guid = "9d3fc2bb-4de1-4805-a5df-3a024b567da9", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Output = new();
}
