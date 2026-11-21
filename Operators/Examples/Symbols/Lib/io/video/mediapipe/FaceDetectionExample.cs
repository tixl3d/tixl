using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace Examples.Lib.io.video.mediapipe;

/// <summary>
/// Working example for the MediaPipe FaceDetection operator - a camera fed into
/// Lib.io.video.mediapipe.FaceDetection, showing its debug view.
/// </summary>
[Guid("ddd9f388-ac70-4ef5-ad14-ead618f3814c")]
internal sealed class FaceDetectionExample : Instance<FaceDetectionExample>
{
    [Output(Guid = "de6c28bd-0d6c-46cc-862b-8b969eb218d2", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Output = new();
}
