using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace Examples.Lib.io.video.mediapipe;

/// <summary>
/// Working example for the MediaPipe GestureRecognition operator - a camera fed into
/// Lib.io.video.mediapipe.GestureRecognition, showing its debug view.
/// </summary>
[Guid("fdbd28d5-85db-44b7-980e-d7531411fa41")]
internal sealed class GestureRecognitionExample : Instance<GestureRecognitionExample>
{
    [Output(Guid = "660d109f-ca00-437a-b8f1-27e5bce8e494", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Output = new();
}
