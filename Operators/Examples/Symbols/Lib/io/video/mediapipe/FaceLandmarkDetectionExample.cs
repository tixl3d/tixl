using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace Examples.Lib.io.video.mediapipe;

/// <summary>
/// Working example for the MediaPipe FaceLandmarkDetection operator - a camera fed into
/// Lib.io.video.mediapipe.FaceLandmarkDetection, showing its debug view.
/// </summary>
[Guid("c5dcd8b9-1c68-4315-82b2-0c1496dd1e70")]
internal sealed class FaceLandmarkDetectionExample : Instance<FaceLandmarkDetectionExample>
{
    [Output(Guid = "08aa5e5b-eeab-4b82-ba79-a2ba6ecfb1ab", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Output = new();
}
