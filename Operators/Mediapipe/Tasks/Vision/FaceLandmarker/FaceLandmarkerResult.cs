using System.Numerics;
using Google.Protobuf.Collections;
using Mediapipe.Tasks.Components.Containers;

namespace Mediapipe.Tasks.Vision.FaceLandmarker;

/// <summary>
///     The face landmarks result from FaceLandmarker, where each vector element represents a single face detected in the
///     image.
/// </summary>
public readonly struct FaceLandmarkerResult
{
    /// <summary>
    ///     Detected face landmarks in normalized image coordinates.
    /// </summary>
    public readonly List<NormalizedLandmarks> FaceLandmarks;

    /// <summary>
    ///     Optional face blendshapes results.
    /// </summary>
    public readonly List<Classifications>? FaceBlendshapes;

    /// <summary>
    ///     Optional facial transformation matrix.
    /// </summary>
    public readonly List<Matrix4x4>? FacialTransformationMatrixes;

    internal FaceLandmarkerResult(List<NormalizedLandmarks> faceLandmarks,
        List<Classifications>? faceBlendshapes, List<Matrix4x4>? facialTransformationMatrixes)
    {
        FaceLandmarks = faceLandmarks;
        FaceBlendshapes = faceBlendshapes;
        FacialTransformationMatrixes = facialTransformationMatrixes;
    }

    public static FaceLandmarkerResult Alloc(int capacity, bool outputFaceBlendshapes = false,
        bool outputFaceTransformationMatrixes = false)
    {
        List<NormalizedLandmarks> faceLandmarks = new(capacity);
        List<Classifications>? faceBlendshapes = outputFaceBlendshapes ? new List<Classifications>(capacity) : null;
        List<Matrix4x4>? facialTransformationMatrixes =
            outputFaceTransformationMatrixes ? new List<Matrix4x4>(capacity) : null;
        return new FaceLandmarkerResult(faceLandmarks, faceBlendshapes, facialTransformationMatrixes);
    }

    public void CloneTo(ref FaceLandmarkerResult destination)
    {
        if (FaceLandmarks == null)
        {
            destination = default;
            return;
        }

        List<NormalizedLandmarks> dstFaceLandmarks =
            destination.FaceLandmarks ?? new List<NormalizedLandmarks>(FaceLandmarks.Count);
        dstFaceLandmarks.Clear();
        dstFaceLandmarks.AddRange(FaceLandmarks);

        List<Classifications>? dstFaceBlendshapes = destination.FaceBlendshapes;
        if (FaceBlendshapes != null)
        {
            dstFaceBlendshapes ??= new List<Classifications>(FaceBlendshapes.Count);
            dstFaceBlendshapes.Clear();
            dstFaceBlendshapes.AddRange(FaceBlendshapes);
        }

        List<Matrix4x4>? dstFacialTransformationMatrixes = destination.FacialTransformationMatrixes;
        if (FacialTransformationMatrixes != null)
        {
            dstFacialTransformationMatrixes ??= new List<Matrix4x4>(FacialTransformationMatrixes.Count);
            dstFacialTransformationMatrixes.Clear();
            dstFacialTransformationMatrixes.AddRange(FacialTransformationMatrixes);
        }

        destination = new FaceLandmarkerResult(dstFaceLandmarks, dstFaceBlendshapes, dstFacialTransformationMatrixes);
    }

    public override string ToString()
    {
        return
            $"{{ \"faceLandmarks\": {Util.Format(FaceLandmarks)}, \"faceBlendshapes\": {Util.Format(FaceBlendshapes)}, \"facialTransformationMatrixes\": {Util.Format(FacialTransformationMatrixes)} }}";
    }
}

internal static class MatrixDataExtension
{
    public static Matrix4x4 ToMatrix4x4(this MatrixData matrixData)
    {
        Matrix4x4 matrix = new();
        RepeatedField<float>? data = matrixData.PackedData;
        // NOTE: z direction is inverted
        if (matrixData.Layout == MatrixData.Types.Layout.RowMajor)
        {
            /*
             * 0,0 0,1 0,2 0,3  0 1 2 3
             * 1,0 1,1 1,2 1,3  4 5 6 7
             * 2,0 2,1 2,2 2,3  8 9 A B
             * 3,0 3,1 3,2 3,3  C D E F
             */

            matrix[0, 0] = data[0];
            matrix[1, 0] = data[4];
            matrix[0, 1] = data[1];
            matrix[1, 1] = data[5];
            matrix[0, 2] = data[2];
            matrix[1, 2] = data[6];
            matrix[0, 3] = data[3];
            matrix[1, 3] = data[7];
            matrix[2, 0] = -data[8];
            matrix[3, 0] = data[12];
            matrix[2, 1] = -data[9];
            matrix[3, 1] = data[13];
            matrix[2, 2] = -data[10];
            matrix[3, 2] = data[14];
            matrix[2, 3] = -data[11];
            matrix[3, 3] = data[15];
        }
        else
        {
            /*
             * 0,0 0,1 0,2 0,3  0 4 8 C
             * 1,0 1,1 1,2 1,3  1 5 9 D
             * 2,0 2,1 2,2 2,3  2 6 A E
             * 3,0 3,1 3,2 3,3  3 7 B F
             */

            matrix[0, 0] = data[0];
            matrix[1, 0] = data[1];
            matrix[0, 1] = data[4];
            matrix[1, 1] = data[5];
            matrix[0, 2] = data[8];
            matrix[1, 2] = data[9];
            matrix[0, 3] = data[12];
            matrix[1, 3] = data[13];
            matrix[2, 0] = -data[2];
            matrix[3, 0] = data[3];
            matrix[2, 1] = -data[6];
            matrix[3, 1] = data[7];
            matrix[2, 2] = -data[10];
            matrix[3, 2] = data[11];
            matrix[2, 3] = -data[14];
            matrix[3, 3] = data[15];
        }

        return matrix;
    }
}