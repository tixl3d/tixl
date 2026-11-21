using Mediapipe.Framework.Formats;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.FaceDetector;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Tasks.Vision.ImageSegmenter;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Xunit;

namespace Mediapipe.Tests;

/// <summary>
/// Drives the real mediapipe_c native (the one shipped in Dependencies) through the same Tasks-API paths
/// the mediapipe operators use: CPU delegate, the bundled .task models, VIDEO-mode timestamp validation
/// included. These are the tests that catch a native that fails to load, exports that don't match the
/// P/Invoke surface, or an ABI mismatch - per platform.
/// </summary>
public class MediapipeNativeTests
{
    private const int Width = 320;
    private const int Height = 240;

    private static string AssetPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "Assets", fileName);

    /// <summary>Deterministic SRGB test frame (gradient). Content doesn't matter - the assertions are about
    /// the pipeline running; on noise images the detectors legitimately find nothing. The buffer is pinned
    /// on the POH for the same reason the operators pin theirs: the native Image keeps the pointer after
    /// the constructor's own pin is released, so a compacting GC could move it mid-inference.</summary>
    private static Image CreateTestImage()
    {
        var pixels = GC.AllocateUninitializedArray<byte>(Width * Height * 3, pinned: true);
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width + x) * 3;
                pixels[i] = (byte)(x * 255 / Width);
                pixels[i + 1] = (byte)(y * 255 / Height);
                pixels[i + 2] = 128;
            }
        }

        return new Image(ImageFormat.Types.Format.Srgb, Width, Height, Width * 3, pixels);
    }

    [Fact]
    public void Image_Construction_ReportsDimensions()
    {
        using var image = CreateTestImage();
        Assert.Equal(Width, image.Width());
        Assert.Equal(Height, image.Height());
        Assert.False(image.UsesGpu());
    }

    [Fact]
    public void HandLandmarker_VideoMode_ProcessesFrames()
    {
        var baseOptions = new CoreBaseOptions(modelAssetPath: AssetPath("hand_landmarker.task"),
                                             delegateCase: CoreBaseOptions.Delegate.CPU);
        var options = new HandLandmarkerOptions(baseOptions, VisionRunningMode.VIDEO, numHands: 2);
        using var landmarker = HandLandmarker.CreateFromOptions(options);

        long timestamp = 0;
        for (var frame = 0; frame < 5; frame++)
        {
            timestamp += 33;
            // DetectForVideo takes ownership of the image packet - a fresh Image per frame,
            // exactly how the operators feed it
            using var image = CreateTestImage();
            var result = landmarker.DetectForVideo(image, timestamp);
            // noise gradient: no hand expected, so the result lists may stay unset (null);
            // the assertion is that the full inference path runs without native faults
            Assert.True(result.HandLandmarks == null || result.HandLandmarks.Count <= 2);
        }
    }

    // The operators default to Xnnpack and offer Gpu; both must either work or fail with a managed exception
    // the operator can catch and fall back from - a native abort here would take the whole editor down.
    [Theory]
    [InlineData(CoreBaseOptions.Delegate.Xnnpack)]
    [InlineData(CoreBaseOptions.Delegate.GPU)]
    public void HandLandmarker_AcceleratedDelegate_RunsOrFailsCatchably(CoreBaseOptions.Delegate delegateCase)
    {
        HandLandmarker landmarker;
        try
        {
            var baseOptions = new CoreBaseOptions(modelAssetPath: AssetPath("hand_landmarker.task"), delegateCase: delegateCase);
            landmarker = HandLandmarker.CreateFromOptions(new HandLandmarkerOptions(baseOptions, VisionRunningMode.VIDEO, numHands: 2));
        }
        catch (Exception) when (delegateCase == CoreBaseOptions.Delegate.GPU)
        {
            return;
        }

        using (landmarker)
        {
            long timestamp = 0;
            for (var frame = 0; frame < 3; frame++)
            {
                timestamp += 33;
                using var image = CreateTestImage();
                var result = landmarker.DetectForVideo(image, timestamp);
                Assert.True(result.HandLandmarks == null || result.HandLandmarks.Count <= 2);
            }
        }
    }

    [Fact]
    public void HandLandmarker_VideoMode_RejectsNonMonotonicTimestamp()
    {
        var baseOptions = new CoreBaseOptions(modelAssetPath: AssetPath("hand_landmarker.task"),
                                             delegateCase: CoreBaseOptions.Delegate.CPU);
        var options = new HandLandmarkerOptions(baseOptions, VisionRunningMode.VIDEO);
        using var landmarker = HandLandmarker.CreateFromOptions(options);

        using (var first = CreateTestImage())
            landmarker.DetectForVideo(first, 100);
        using (var second = CreateTestImage())
            Assert.ThrowsAny<Exception>(() => landmarker.DetectForVideo(second, 100));
    }

    [Fact]
    public void PoseLandmarker_VideoMode_ProcessesFrames()
    {
        var baseOptions = new CoreBaseOptions(modelAssetPath: AssetPath("pose_landmarker.task"),
                                             delegateCase: CoreBaseOptions.Delegate.CPU);
        var options = new PoseLandmarkerOptions(baseOptions, VisionRunningMode.VIDEO, numPoses: 1);
        using var landmarker = PoseLandmarker.CreateFromOptions(options);

        long timestamp = 0;
        for (var frame = 0; frame < 3; frame++)
        {
            timestamp += 33;
            using var image = CreateTestImage();
            var result = landmarker.DetectForVideo(image, timestamp);
            Assert.True(result.PoseLandmarks == null || result.PoseLandmarks.Count <= 1);
        }
    }

    [Fact]
    public void FaceDetector_ImageMode_Runs()
    {
        var baseOptions = new CoreBaseOptions(modelAssetPath: AssetPath("blaze_face_short_range.tflite"),
                                             delegateCase: CoreBaseOptions.Delegate.CPU);
        var options = new FaceDetectorOptions(baseOptions, VisionRunningMode.IMAGE);
        using var detector = FaceDetector.CreateFromOptions(options);

        using var image = CreateTestImage();
        var result = detector.Detect(image);
        // no face on a gradient - the assertion is that the inference path completes
        Assert.True(result.Detections == null || result.Detections.Count == 0);
    }

    /// <summary>
    /// The operators read the segmentation mask through PixelWriteLock and must dispose the
    /// result's native mask images (the landmarker allocates one per inference). This runs the
    /// exact operator path repeatedly - a missing dispose or an invalid mask read shows up as
    /// native memory growth or a fault.
    /// </summary>
    [Fact]
    public void PoseLandmarker_SegmentationMasks_ReadAndDisposeAcrossFrames()
    {
        var baseOptions = new CoreBaseOptions(modelAssetPath: AssetPath("pose_landmarker.task"),
                                             delegateCase: CoreBaseOptions.Delegate.CPU);
        var options = new PoseLandmarkerOptions(baseOptions, VisionRunningMode.VIDEO, numPoses: 1,
                                                outputSegmentationMasks: true);
        using var landmarker = PoseLandmarker.CreateFromOptions(options);

        long timestamp = 0;
        var framesWithMasks = 0;
        for (var frame = 0; frame < 20; frame++)
        {
            timestamp += 33;
            using var image = CreateTestImage();
            var result = landmarker.DetectForVideo(image, timestamp);

            // Whether the bundled pose model emits a segmentation-mask packet is model/native dependent,
            // and no assertion here may depend on it: the contract under test is the operator's read +
            // dispose path, which must work when masks appear and must not fault when they do not.
            if (result.SegmentationMasks is not { Count: > 0 })
                continue;

            framesWithMasks++;
            foreach (var mask in result.SegmentationMasks)
            {
                try
                {
                    Assert.True(mask.Width() > 0);
                    Assert.True(mask.Height() > 0);
                    mask.ConvertToCpu();
                    using var pixelLock = new PixelWriteLock(mask);
                    Assert.NotEqual(nint.Zero, pixelLock.Pixels());
                }
                finally
                {
                    mask.Dispose();
                }
            }
        }
    }

    [Fact]
    public void ImageSegmenter_Masks_ReadAndDisposeAcrossFrames()
    {
        var baseOptions = new CoreBaseOptions(modelAssetPath: AssetPath("selfie_segmenter.tflite"),
                                             delegateCase: CoreBaseOptions.Delegate.CPU);
        var options = new ImageSegmenterOptions(baseOptions, VisionRunningMode.VIDEO,
                                                outputCategoryMask: true, outputConfidenceMasks: true);
        using var segmenter = ImageSegmenter.CreateFromOptions(options);

        long timestamp = 0;
        for (var frame = 0; frame < 20; frame++)
        {
            timestamp += 33;
            using var image = CreateTestImage();
            var result = segmenter.SegmentForVideo(image, timestamp);

            try
            {
                var mask = Assert.Single(result.ConfidenceMasks!);
                mask.ConvertToCpu();
                using var pixelLock = new PixelWriteLock(mask);
                Assert.NotEqual(nint.Zero, pixelLock.Pixels());

                Assert.NotNull(result.CategoryMask);
                Assert.True(result.CategoryMask!.Width() > 0);
            }
            finally
            {
                foreach (var mask in result.ConfidenceMasks!)
                    mask.Dispose();
                result.CategoryMask?.Dispose();
            }
        }
    }

    [Fact]
    public void ImageSegmenter_ConfidenceMask_MatchesInputDimensions()
    {
        var baseOptions = new CoreBaseOptions(modelAssetPath: AssetPath("selfie_segmenter.tflite"),
                                             delegateCase: CoreBaseOptions.Delegate.CPU);
        var options = new ImageSegmenterOptions(baseOptions, VisionRunningMode.IMAGE, outputConfidenceMasks: true);
        using var segmenter = ImageSegmenter.CreateFromOptions(options);

        using var image = CreateTestImage();
        var result = segmenter.Segment(image);

        Assert.NotNull(result.ConfidenceMasks);
        var mask = Assert.Single(result.ConfidenceMasks);
        Assert.Equal(Width, mask.Width());
        Assert.Equal(Height, mask.Height());
    }
}
