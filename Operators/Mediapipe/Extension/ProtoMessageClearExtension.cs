using Mediapipe.Tasks.Vision.FaceGeometry.Proto;

namespace Mediapipe.Extension;

public static class ProtoMessageClearExtension
{
    public static void Clear(this ClassificationList classificationList)
    {
        classificationList.Classification.Clear();
    }

    public static void Clear(this Detection detection)
    {
        detection.Label.Clear();
        detection.LabelId.Clear();
        detection.Score.Clear();
        detection.LocationData.Clear();
        detection.ClearFeatureTag();
        detection.ClearTrackId();
        detection.ClearDetectionId();
        detection.AssociatedDetections.Clear();
        detection.DisplayName.Clear();
        detection.ClearTimestampUsec();
    }

    public static void Clear(this LocationData locationData)
    {
        locationData.ClearFormat();
        if (locationData.BoundingBox != null)
        {
            locationData.BoundingBox.ClearXmin();
            locationData.BoundingBox.ClearYmin();
            locationData.BoundingBox.ClearWidth();
            locationData.BoundingBox.ClearHeight();
        }

        if (locationData.RelativeBoundingBox != null)
        {
            locationData.RelativeBoundingBox.ClearXmin();
            locationData.RelativeBoundingBox.ClearYmin();
            locationData.RelativeBoundingBox.ClearWidth();
            locationData.RelativeBoundingBox.ClearHeight();
        }

        if (locationData.Mask != null)
        {
            locationData.Mask.ClearWidth();
            locationData.Mask.ClearHeight();
            locationData.Mask.Rasterization.Interval.Clear();
        }

        locationData.RelativeKeypoints.Clear();
    }

    public static void Clear(this NormalizedLandmarkList landmarkList)
    {
        landmarkList.Landmark.Clear();
    }

    public static void Clear(this FaceGeometry faceGeometry)
    {
        faceGeometry.Mesh?.Clear();
        faceGeometry.PoseTransformMatrix?.Clear();
    }

    public static void Clear(this MatrixData matrixData)
    {
        matrixData.ClearRows();
        matrixData.ClearCols();
        matrixData.PackedData.Clear();
        matrixData.ClearLayout();
    }

    public static void Clear(this Mesh3d mesh3d)
    {
        mesh3d.ClearVertexType();
        mesh3d.ClearPrimitiveType();
        mesh3d.VertexBuffer.Clear();
        mesh3d.IndexBuffer.Clear();
    }
}