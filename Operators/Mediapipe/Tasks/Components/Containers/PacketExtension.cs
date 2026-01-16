using Mediapipe.Framework;
using Mediapipe.PInvoke;
using Mediapipe.TranMarshal;

namespace Mediapipe.Tasks.Components.Containers;

public static class PacketExtension
{
    public static void Get(this Packet<ClassificationResult> packet, ref ClassificationResult value)
    {
        UnsafeNativeMethods
            .mp_Packet__GetClassificationResult(packet.MpPtr, out NativeClassificationResult classificationResult)
            .Assert();
        ClassificationResult.Copy(classificationResult, ref value);
        classificationResult.Dispose();
    }

    public static void Get(this Packet<List<Classifications>> packet, List<Classifications> outs)
    {
        UnsafeNativeMethods
            .mp_Packet__GetClassificationsVector(packet.MpPtr, out NativeClassificationResult classificationResult)
            .Assert();
        ClassificationResult tmp = new(outs, null);
        ClassificationResult.Copy(classificationResult, ref tmp);
        classificationResult.Dispose();
    }

    [Obsolete("Use Get instead")]
    public static void GetClassificationsVector(this Packet<List<Classifications>> packet, List<Classifications> outs)
    {
        Get(packet, outs);
    }

    public static void Get(this Packet<DetectionResult> packet, ref DetectionResult value)
    {
        UnsafeNativeMethods.mp_Packet__GetDetectionResult(packet.MpPtr, out NativeDetectionResult detectionResult)
            .Assert();
        DetectionResult.Copy(detectionResult, ref value);
        detectionResult.Dispose();
    }

    public static void Get(this Packet<List<ClassificationResult>> packet, List<ClassificationResult> outs)
    {
        UnsafeNativeMethods
            .mp_Packet__GetClassificationResultVector(packet.MpPtr,
                out NativeClassificationResultArray classificationResults).Assert();
        outs.FillWith(classificationResults);
        classificationResults.Dispose();
    }

    [Obsolete("Use Get instead")]
    public static void GetDetectionResult(this Packet<DetectionResult> packet, ref DetectionResult value)
    {
        Get(packet, ref value);
    }

    public static void Get(this Packet<List<Landmarks>> packet, List<Landmarks> outs)
    {
        UnsafeNativeMethods.mp_Packet__GetLandmarksVector(packet.MpPtr, out NativeLandmarksArray landmarksArray)
            .Assert();
        outs.FillWith(landmarksArray);
        landmarksArray.Dispose();
    }

    [Obsolete("Use Get instead")]
    public static void GetLandmarksList(this Packet<List<Landmarks>> packet, List<Landmarks> outs)
    {
        Get(packet, outs);
    }

    public static void Get(this Packet<List<NormalizedLandmarks>> packet, List<NormalizedLandmarks> outs)
    {
        UnsafeNativeMethods
            .mp_Packet__GetNormalizedLandmarksVector(packet.MpPtr, out NativeNormalizedLandmarksArray landmarksArray)
            .Assert();
        outs.FillWith(landmarksArray);
        landmarksArray.Dispose();
    }

    [Obsolete("Use Get instead")]
    public static void GetNormalizedLandmarksList(this Packet<List<NormalizedLandmarks>> packet,
        List<NormalizedLandmarks> outs)
    {
        Get(packet, outs);
    }
}