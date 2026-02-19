using Mediapipe.Framework.Formats;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Core.Proto;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceDetector.Proto;
using Mediapipe.Tasks.Vision.FaceLandmarker.Proto;

namespace Mediapipe.Tasks.Vision.FaceLandmarker;

/// <summary>
///     Options for the face landmarker task.
/// </summary>
public sealed class FaceLandmarkerOptions(
    CoreBaseOptions baseOptions,
    VisionRunningMode runningMode = VisionRunningMode.IMAGE,
    int numFaces = 1,
    float minFaceDetectionConfidence = 0.5f,
    float minFacePresenceConfidence = 0.5f,
    float minTrackingConfidence = 0.5f,
    bool outputFaceBlendshapes = false,
    bool outputFaceTransformationMatrixes = false,
    FaceLandmarkerOptions.ResultCallbackFunc? resultCallback = null) : ITaskOptions
{
    /// <param name="faceLandmarksResult">
    ///     The face landmarks detection results.
    /// </param>
    /// <param name="image">
    ///     The input image that the face landmarker runs on.
    /// </param>
    /// <param name="timestampMillisec">
    ///     The input timestamp in milliseconds.
    /// </param>
    public delegate void ResultCallbackFunc(FaceLandmarkerResult faceLandmarksResult, Image image,
        long timestampMillisec);

    /// <summary>
    ///     Base options for the hand landmarker task.
    /// </summary>
    public CoreBaseOptions BaseOptions { get; } = baseOptions;

    /// <summary>
    ///     The running mode of the task. Default to the image mode.
    ///     FaceLandmarker has three running modes:
    ///     <list type="number">
    ///         <item>
    ///             <description>The image mode for detecting face landmarks on single image inputs.</description>
    ///         </item>
    ///         <item>
    ///             <description>The video mode for detecting face landmarks on the decoded frames of a video.</description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 The live stream mode or detecting face landmarks on the live stream of input data, such as from camera.
    ///                 In this mode, the <see cref="ResultCallback" /> below must be specified to receive the detection
    ///                 results asynchronously.
    ///             </description>
    ///         </item>
    ///     </list>
    /// </summary>
    public VisionRunningMode RunningMode { get; } = runningMode;

    /// <summary>
    ///     The maximum number of faces that can be detected by the face detector.
    /// </summary>
    public int NumFaces { get; } = numFaces;

    /// <summary>
    ///     The minimum confidence score for the face detection to be considered successful.
    /// </summary>
    public float MinFaceDetectionConfidence { get; } = minFaceDetectionConfidence;

    /// <summary>
    ///     The minimum confidence score of face presence score in the face landmark detection.
    /// </summary>
    public float MinFacePresenceConfidence { get; } = minFacePresenceConfidence;

    /// <summary>
    ///     The minimum confidence score for the face tracking to be considered successful.
    /// </summary>
    public float MinTrackingConfidence { get; } = minTrackingConfidence;

    /// <summary>
    ///     Whether FaceLandmarker outputs face blendshapes classification.
    ///     Face blendshapes are used for rendering the 3D face model.
    /// </summary>
    public bool OutputFaceBlendshapes { get; } = outputFaceBlendshapes;

    /// <summary>
    ///     Whether FaceLandmarker outputs facial transformation_matrix.
    ///     Facial transformation matrix is used to transform the face landmarks in canonical face to the detected face,
    ///     so that users can apply face effects on the detected landmarks.
    /// </summary>
    public bool OutputFaceTransformationMatrixes { get; } = outputFaceTransformationMatrixes;

    /// <summary>
    ///     The user-defined result callback for processing live stream data.
    ///     The result callback should only be specified when the running mode is set to the live stream mode.
    /// </summary>
    public ResultCallbackFunc? ResultCallback { get; } = resultCallback;

    CalculatorOptions ITaskOptions.ToCalculatorOptions()
    {
        CalculatorOptions options = new();
        options.SetExtension(FaceLandmarkerGraphOptions.Extensions.Ext, ToProto());
        return options;
    }

    internal FaceLandmarkerGraphOptions ToProto()
    {
        BaseOptions baseOptionsProto = BaseOptions.ToProto();
        baseOptionsProto.UseStreamMode = RunningMode != VisionRunningMode.IMAGE;

        return new FaceLandmarkerGraphOptions
        {
            BaseOptions = baseOptionsProto,
            FaceDetectorGraphOptions = new FaceDetectorGraphOptions
            {
                MinDetectionConfidence = MinFaceDetectionConfidence,
                NumFaces = NumFaces
            },
            FaceLandmarksDetectorGraphOptions = new FaceLandmarksDetectorGraphOptions
            {
                MinDetectionConfidence = MinFacePresenceConfidence
            },
            MinTrackingConfidence = MinTrackingConfidence
        };
    }
}