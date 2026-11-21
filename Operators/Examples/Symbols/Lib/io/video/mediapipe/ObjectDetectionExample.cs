using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace Examples.Lib.io.video.mediapipe;

/// <summary>
/// Working example for the MediaPipe ObjectDetection operator - a camera fed into
/// Lib.io.video.mediapipe.ObjectDetection, showing its debug view.
/// </summary>
[Guid("2261aae2-88cd-471a-a41f-7d82acf7e3d1")]
internal sealed class ObjectDetectionExample : Instance<ObjectDetectionExample>
{
    [Output(Guid = "0811a904-ed9c-4903-8401-71704cdc78ee", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Output = new();
}
