using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace Examples.Lib.io.video.mediapipe;

/// <summary>
/// Working example for the MediaPipe PoseLandmarkDetection operator - a camera fed into
/// Lib.io.video.mediapipe.PoseLandmarkDetection, showing its debug view.
/// </summary>
[Guid("f77b3731-f87c-4dd9-822a-4509de8cb5eb")]
internal sealed class PoseLandmarkDetectionExample : Instance<PoseLandmarkDetectionExample>
{
    [Output(Guid = "6cb30146-5f67-483d-bc32-64add6c62e4d", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Output = new();
}
