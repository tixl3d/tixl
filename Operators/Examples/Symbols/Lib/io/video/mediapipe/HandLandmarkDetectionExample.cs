using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace Examples.Lib.io.video.mediapipe;

/// <summary>
/// Working example for the MediaPipe HandLandmarkDetection operator - a camera fed into
/// Lib.io.video.mediapipe.HandLandmarkDetection, showing its debug view.
/// </summary>
[Guid("f34db825-436a-41bb-b33b-b433e102cbcb")]
internal sealed class HandLandmarkDetectionExample : Instance<HandLandmarkDetectionExample>
{
    [Output(Guid = "964451c1-7773-4e86-95da-013497b9d2c9", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Output = new();
}
