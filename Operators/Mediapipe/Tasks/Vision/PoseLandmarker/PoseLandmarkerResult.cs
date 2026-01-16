using Mediapipe.Framework.Formats;
using Mediapipe.Tasks.Components.Containers;

namespace Mediapipe.Tasks.Vision.PoseLandmarker;

/// <summary>
///     The pose landmarks detection result from PoseLandmarker, where each vector element represents a single pose
///     detected in the image.
/// </summary>
public readonly struct PoseLandmarkerResult
{
    /// <summary>
    ///     Detected pose landmarks in normalized image coordinates.
    /// </summary>
    public readonly List<NormalizedLandmarks> PoseLandmarks;

    /// <summary>
    ///     Detected pose landmarks in world coordinates.
    /// </summary>
    public readonly List<Landmarks> PoseWorldLandmarks;

    /// <summary>
    ///     Optional segmentation masks for pose.
    ///     Each <see cref="Image" /> in <see cref="SegmentationMasks" /> must be disposed after use.
    /// </summary>
    public readonly List<Image>? SegmentationMasks;

    internal PoseLandmarkerResult(List<NormalizedLandmarks> poseLandmarks,
        List<Landmarks> poseWorldLandmarks, List<Image>? segmentationMasks = null)
    {
        PoseLandmarks = poseLandmarks;
        PoseWorldLandmarks = poseWorldLandmarks;
        SegmentationMasks = segmentationMasks;
    }

    public static PoseLandmarkerResult Alloc(int capacity, bool outputSegmentationMasks = false)
    {
        List<NormalizedLandmarks> poseLandmarks = new(capacity);
        List<Landmarks> poseWorldLandmarks = new(capacity);
        List<Image>? segmentationMasks = outputSegmentationMasks ? new List<Image>(capacity) : null;
        return new PoseLandmarkerResult(poseLandmarks, poseWorldLandmarks, segmentationMasks);
    }

    /// <remarks>
    ///     Each <see cref="Image" /> in <see cref="SegmentationMasks" /> will be moved to <paramref name="destination" />.
    /// </remarks>
    public void CloneTo(ref PoseLandmarkerResult destination)
    {
        if (PoseLandmarks == null)
        {
            destination = default;
            return;
        }

        List<NormalizedLandmarks> dstPoseLandmarks =
            destination.PoseLandmarks ?? new List<NormalizedLandmarks>(PoseLandmarks.Count);
        dstPoseLandmarks.Clear();
        dstPoseLandmarks.AddRange(PoseLandmarks);

        List<Landmarks> dstPoseWorldLandmarks =
            destination.PoseWorldLandmarks ?? new List<Landmarks>(PoseWorldLandmarks.Count);
        dstPoseWorldLandmarks.Clear();
        dstPoseWorldLandmarks.AddRange(PoseWorldLandmarks);

        List<Image>? dstSegmentationMasks = destination.SegmentationMasks;
        if (SegmentationMasks != null)
        {
            dstSegmentationMasks ??= new List<Image>(SegmentationMasks.Count);
            foreach (Image mask in dstSegmentationMasks) mask.Dispose();
            dstSegmentationMasks.Clear();
            dstSegmentationMasks.AddRange(SegmentationMasks);
            SegmentationMasks.Clear();
        }

        destination = new PoseLandmarkerResult(dstPoseLandmarks, dstPoseWorldLandmarks, dstSegmentationMasks);
    }

    public override string ToString()
    {
        return
            $"{{ \"poseLandmarks\": {Util.Format(PoseLandmarks)}, \"poseWorldLandmarks\": {Util.Format(PoseWorldLandmarks)}, \"segmentationMasks\": {Util.Format(SegmentationMasks)} }}";
    }
}