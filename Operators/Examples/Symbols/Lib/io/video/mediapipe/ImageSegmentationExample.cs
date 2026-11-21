using T3.Core.Operator;
using T3.Core.Operator.Attributes;
using T3.Core.DataTypes;
using T3.Core.Operator.Slots;

namespace Examples.Lib.io.video.mediapipe;

/// <summary>
/// Working example for the MediaPipe ImageSegmentation operator - a camera fed into
/// Lib.io.video.mediapipe.ImageSegmentation, showing its debug view.
/// </summary>
[Guid("41d5a992-673f-491e-b912-a55334e4dfec")]
internal sealed class ImageSegmentationExample : Instance<ImageSegmentationExample>
{
    [Output(Guid = "4f7c9558-a970-42b3-a57d-3cb93be4ab07", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Texture2D> Output = new();
}
