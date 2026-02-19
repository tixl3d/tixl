using Mediapipe.Extension;
using Mediapipe.Framework;
using Mediapipe.Framework.Formats;
using Mediapipe.Framework.Packet;
using Mediapipe.Gpu;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using System.Numerics;
using Image = Mediapipe.Framework.Formats.Image;

namespace Mediapipe.Tasks.Vision.FaceLandmarker;

public sealed class FaceLandmarker : BaseVisionTaskApi
{
    private const string ImageInStreamName = "image_in";
    private const string ImageOutStreamName = "image_out";
    private const string ImageTag = "IMAGE";
    private const string NormRectStreamName = "norm_rect_in";
    private const string NormRectTag = "NORM_RECT";
    private const string NormLandmarksStreamName = "norm_landmarks";
    private const string NormLandmarksTag = "NORM_LANDMARKS";
    private const string BlendshapesStreamName = "blendshapes";
    private const string BlendshapesTag = "BLENDSHAPES";
    private const string FaceGeometryStreamName = "face_geometry";
    private const string FaceGeometryTag = "FACE_GEOMETRY";
    private const string TaskGraphName = "mediapipe.tasks.vision.face_landmarker.FaceLandmarkerGraph";

    private const int MicroSecondsPerMillisecond = 1000;
    private readonly List<FaceGeometry.Proto.FaceGeometry>? _faceGeometriesForRead;

    private readonly NormalizedRect _normalizedRect = new();

    private FaceLandmarker(
        CalculatorGraphConfig graphConfig,
        VisionRunningMode runningMode,
        GpuResources? gpuResources,
        List<FaceGeometry.Proto.FaceGeometry>? faceGeometriesForRead,
        TaskRunner.PacketsCallback? packetCallback) : base(graphConfig, runningMode, gpuResources, packetCallback)
    {
        _faceGeometriesForRead = faceGeometriesForRead;
    }

    /// <summary>
    ///     Creates an <see cref="FaceLandmarker" /> object from a TensorFlow Lite model and the default
    ///     <see cref="FaceLandmarkerOptions" />.
    ///     Note that the created <see cref="FaceLandmarker" /> instance is in image mode,
    ///     for detecting face landmarks on single image inputs.
    /// </summary>
    /// <param name="modelPath">Path to the model.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="FaceLandmarker" /> object that's created from the model and the default
    ///     <see cref="FaceLandmarkerOptions" />.
    /// </returns>
    public static FaceLandmarker CreateFromModelPath(string modelPath, GpuResources? gpuResources = null)
    {
        CoreBaseOptions baseOptions = new(modelAssetPath: modelPath);
        FaceLandmarkerOptions options = new(baseOptions);
        return CreateFromOptions(options, gpuResources);
    }

    /// <summary>
    ///     Creates the <see cref="FaceLandmarker" /> object from <paramref name="FaceLandmarkerOptions" />.
    /// </summary>
    /// <param name="options">Options for the face landmarker task.</param>
    /// <param name="gpuResources">
    ///     <see cref="GpuResources" /> to set to the underlying <see cref="CalculatorGraph" />.
    ///     To share the GL context with MediaPipe, <see cref="GlCalculatorHelper.InitializeForTest" /> must be called with it.
    /// </param>
    /// <returns>
    ///     <see cref="FaceLandmarker" /> object that's created from <paramref name="options" />.
    /// </returns>
    public static FaceLandmarker CreateFromOptions(FaceLandmarkerOptions options, GpuResources? gpuResources = null)
    {
        List<string> outputStreams = new()
        {
            string.Join(":", NormLandmarksTag, NormLandmarksStreamName),
            string.Join(":", ImageTag, ImageOutStreamName)
        };
        if (options.OutputFaceBlendshapes)
            outputStreams.Add(string.Join(":", BlendshapesTag, BlendshapesStreamName));
        if (options.OutputFaceTransformationMatrixes)
            outputStreams.Add(string.Join(":", FaceGeometryTag, FaceGeometryStreamName));
        TaskInfo<FaceLandmarkerOptions> taskInfo = new(
            TaskGraphName,
            [
                string.Join(":", ImageTag, ImageInStreamName),
                string.Join(":", NormRectTag, NormRectStreamName)
            ],
            outputStreams,
            options);

        List<FaceGeometry.Proto.FaceGeometry>? faceGeometriesForRead = options.OutputFaceTransformationMatrixes
            ? new List<FaceGeometry.Proto.FaceGeometry>(options.NumFaces)
            : null;

        TaskRunner.PacketsCallback? packetsCallback = null;
        if (options.RunningMode == VisionRunningMode.LIVE_STREAM)
        {
            packetsCallback = BuildPacketsCallback(options, faceGeometriesForRead);
        }

        return new FaceLandmarker(
            taskInfo.GenerateGraphConfig(options.RunningMode == VisionRunningMode.LIVE_STREAM),
            options.RunningMode, gpuResources,
            faceGeometriesForRead,
            packetsCallback);
    }

    /// <summary>
    ///     Performs face landmarks detection on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="FaceLandmarker" /> is created with the image running mode.
    ///     The image can be of any size with format RGB or RGBA.
    /// </summary>
    /// <param name="image">MediaPipe Image.</param>
    /// <param name="imageProcessingOptions">Options for image processing.</param>
    /// <returns>
    ///     The face landmarks detection results.
    /// </returns>
    public FaceLandmarkerResult Detect(Image image, ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = DetectInternal(image, imageProcessingOptions);

        FaceLandmarkerResult result = default;
        _ = TryBuildFaceLandmarkerResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs face landmarks detection on the provided MediaPipe Image.
    ///     Only use this method when the <see cref="FaceLandmarker" /> is created with the image running mode.
    ///     The image can be of any size with format RGB or RGBA.
    /// </summary>
    /// <remarks>
    ///     When faces are not found, <paramref name="result" /> won't be overwritten.
    /// </remarks>
    /// <param name="image">MediaPipe Image.</param>
    /// <param name="imageProcessingOptions">Options for image processing.</param>
    /// <param name="result">
    ///     <see cref="FaceLandmarkerResult" /> to which the result will be written.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if some faces are detected, <see langword="false" /> otherwise.
    /// </returns>
    public bool TryDetect(Image image, ImageProcessingOptions? imageProcessingOptions, ref FaceLandmarkerResult result)
    {
        using PacketMap outputPackets = DetectInternal(image, imageProcessingOptions);
        return TryBuildFaceLandmarkerResult(outputPackets, ref result);
    }

    private PacketMap DetectInternal(Image image, ImageProcessingOptions? imageProcessingOptions)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);

        PacketMap packetMap = new();
        packetMap.Emplace(ImageInStreamName, PacketHelper.CreateImage(image));
        packetMap.Emplace(NormRectStreamName, PacketHelper.CreateProto(_normalizedRect));

        return ProcessImageData(packetMap);
    }

    /// <summary>
    ///     Performs face landmarks detection on the provided video frames.
    ///     Only use this method when the FaceLandmarker is created with the video
    ///     running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <returns>
    ///     The face landmarks detection results.
    /// </returns>
    public FaceLandmarkerResult DetectForVideo(Image image, long timestampMillisec,
        ImageProcessingOptions? imageProcessingOptions = null)
    {
        using PacketMap outputPackets = DetectForVideoInternal(image, timestampMillisec, imageProcessingOptions);

        FaceLandmarkerResult result = default;
        _ = TryBuildFaceLandmarkerResult(outputPackets, ref result);
        return result;
    }

    /// <summary>
    ///     Performs face landmarks detection on the provided video frames.
    ///     Only use this method when the FaceLandmarker is created with the video
    ///     _running mode. It's required to provide the video frame's timestamp (in
    ///     milliseconds) along with the video frame. The input timestamps should be
    ///     monotonically increasing for adjacent calls of this method.
    /// </summary>
    /// <remarks>
    ///     When faces are not found, <paramref name="result" /> won't be overwritten.
    /// </remarks>
    /// <param name="result">
    ///     <see cref="FaceLandmarkerResult" /> to which the result will be written.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if some faces are detected, <see langword="false" /> otherwise.
    /// </returns>
    public bool TryDetectForVideo(Image image, long timestampMillisec, ImageProcessingOptions? imageProcessingOptions,
        ref FaceLandmarkerResult result)
    {
        using PacketMap outputPackets = DetectForVideoInternal(image, timestampMillisec, imageProcessingOptions);
        return TryBuildFaceLandmarkerResult(outputPackets, ref result);
    }

    private PacketMap DetectForVideoInternal(Image image, long timestampMillisec,
        ImageProcessingOptions? imageProcessingOptions = null)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);
        long timestampMicrosec = timestampMillisec * MicroSecondsPerMillisecond;

        PacketMap packetMap = new();
        packetMap.Emplace(ImageInStreamName, PacketHelper.CreateImageAt(image, timestampMicrosec));
        packetMap.Emplace(NormRectStreamName, PacketHelper.CreateProtoAt(_normalizedRect, timestampMicrosec));

        return ProcessVideoData(packetMap);
    }

    /// <summary>
    ///     Sends live image data to perform face landmarks detection.
    ///     Only use this method when the FaceLandmarker is created with the live stream
    ///     running mode. The input timestamps should be monotonically increasing for
    ///     adjacent calls of this method. This method will return immediately after the
    ///     input image is accepted. The results will be available via the
    ///     <see cref="FaceLandmarkerOptions.ResultCallbackFunc" /> provided in the <see cref="FaceLandmarkerOptions" />.
    ///     The <see cref="DetectAsync" /> method is designed to process live stream data such as camera
    ///     input. To lower the overall latency, face landmarker may drop the input
    ///     images if needed. In other words, it's not guaranteed to have output per
    ///     input image.
    /// </summary>
    public void DetectAsync(Image image, long timestampMillisec, ImageProcessingOptions? imageProcessingOptions = null)
    {
        ConfigureNormalizedRect(_normalizedRect, imageProcessingOptions, image, false);
        long timestampMicrosec = timestampMillisec * MicroSecondsPerMillisecond;

        PacketMap packetMap = new();
        packetMap.Emplace(ImageInStreamName, PacketHelper.CreateImageAt(image, timestampMicrosec));
        packetMap.Emplace(NormRectStreamName, PacketHelper.CreateProtoAt(_normalizedRect, timestampMicrosec));

        SendLiveStreamData(packetMap);
    }

    private bool TryBuildFaceLandmarkerResult(PacketMap outputPackets, ref FaceLandmarkerResult result)
    {
        return TryBuildFaceLandmarkerResult(outputPackets, _faceGeometriesForRead, ref result);
    }

    private static TaskRunner.PacketsCallback? BuildPacketsCallback(FaceLandmarkerOptions options,
        List<FaceGeometry.Proto.FaceGeometry>? faceGeometriesForRead)
    {
        var resultCallback = options.ResultCallback;

        FaceLandmarkerResult faceLandmarkerResult = FaceLandmarkerResult.Alloc(options.NumFaces,
            options.OutputFaceBlendshapes, options.OutputFaceTransformationMatrixes);

        return outputPackets =>
        {
            using Packet<Image>? outImagePacket = outputPackets.At<Image>(ImageOutStreamName);
            if (outImagePacket == null! || outImagePacket.IsEmpty()) return;

            using Image image = outImagePacket.Get();
            long timestamp = outImagePacket.TimestampMicroseconds() / MicroSecondsPerMillisecond;

            // Check if we have landmark data regardless of face detection results
            if (TryBuildFaceLandmarkerResult(outputPackets, faceGeometriesForRead, ref faceLandmarkerResult))
                resultCallback!(faceLandmarkerResult, image, timestamp);
            else
            {
                // Even if no faces are detected, we should still call the callback with empty result
                var emptyResult = new FaceLandmarkerResult([], null, null);
                resultCallback!(emptyResult, image, timestamp);
            }
        };
    }

    private static void GetFaceGeometryList(Packet<List<FaceGeometry.Proto.FaceGeometry>> packet,
        List<FaceGeometry.Proto.FaceGeometry> outs)
    {
        foreach (FaceGeometry.Proto.FaceGeometry geometry in outs) geometry.Clear();

        int size = packet.WriteTo(FaceGeometry.Proto.FaceGeometry.Parser, outs);
        outs.RemoveRange(size, outs.Count - size);
    }

    private static bool TryBuildFaceLandmarkerResult(PacketMap outputPackets,
        List<FaceGeometry.Proto.FaceGeometry>? faceGeometriesForRead,
        ref FaceLandmarkerResult result)
    {
        using Packet<List<NormalizedLandmarks>>? faceLandmarksPacket =
            outputPackets.At<List<NormalizedLandmarks>>(NormLandmarksStreamName);
        if (faceLandmarksPacket.IsEmpty()) return false;

        List<NormalizedLandmarks> faceLandmarks = result.FaceLandmarks ?? [];
        faceLandmarksPacket.Get(faceLandmarks);

        List<Classifications>? faceBlendshapesList = result.FaceBlendshapes;
        using Packet<List<Classifications>>? faceBlendshapesPacket =
            outputPackets.At<List<Classifications>>(BlendshapesStreamName);
        if (faceBlendshapesPacket != null)
        {
            faceBlendshapesList ??= [];
            faceBlendshapesPacket.Get(faceBlendshapesList);
        }

        List<Matrix4x4>? faceTransformationMatrixes = result.FacialTransformationMatrixes;
        using Packet<List<FaceGeometry.Proto.FaceGeometry>>? faceTransformationMatrixesPacket =
            outputPackets.At<List<FaceGeometry.Proto.FaceGeometry>>(FaceGeometryStreamName);
        if (faceTransformationMatrixesPacket != null && faceGeometriesForRead != null)
        {
            GetFaceGeometryList(faceTransformationMatrixesPacket, faceGeometriesForRead);
            faceTransformationMatrixes ??= new List<Matrix4x4>(faceGeometriesForRead.Count);

            faceTransformationMatrixes.Clear();
            foreach (FaceGeometry.Proto.FaceGeometry faceGeometry in faceGeometriesForRead)
                faceTransformationMatrixes.Add(faceGeometry.PoseTransformMatrix.ToMatrix4x4());
        }

        result = new FaceLandmarkerResult(faceLandmarks, faceBlendshapesList, faceTransformationMatrixes);
        return true;
    }
}
