// Exercises the OpenCV code paths the operators rely on, on whatever platform this runs: loads the
// native extern library (the real availability gate for [CameraCalibrator] and friends), then runs
// calibration-style corner detection and undistortion plus the point scanner's contour pass.
// Exit code 0 = OpenCV usable on this platform.
using OpenCvSharp;

Console.WriteLine($"OpenCV smoke on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");

using var board = new Mat(480, 640, MatType.CV_8UC3, Scalar.All(255));
const int square = 80;
for (var y = 0; y < 5; y++)
for (var x = 0; x < 7; x++)
    if ((x + y) % 2 == 0)
        Cv2.Rectangle(board, new Rect(40 + x * square, 40 + y * square, square, square), Scalar.Black, -1);

var found = Cv2.FindChessboardCorners(board, new Size(6, 4), out var corners);
Console.WriteLine($"FindChessboardCorners: found={found}, corners={corners.Length}");
if (!found || corners.Length != 24)
    return Fail($"expected the 6x4 checkerboard with 24 corners, got found={found}, {corners.Length} corners");

using var gray = new Mat();
Cv2.CvtColor(board, gray, ColorConversionCodes.BGR2GRAY);
Console.WriteLine($"CvtColor: {gray.Size().Width}x{gray.Size().Height}");

// CameraCalibrator's lens-correction path needs 1xN distortion coefficients.
using var cameraMatrix = Mat.Eye(3, 3, MatType.CV_64FC1);
using var distCoeffs = InputArray.Create(new double[] { 0.1, 0, 0, 0, 0 });
using var map1 = new Mat();
using var map2 = new Mat();
Cv2.InitUndistortRectifyMap(cameraMatrix, distCoeffs, cameraMatrix, cameraMatrix, board.Size(), MatType.CV_32FC1, map1, map2);
using var corrected = new Mat();
Cv2.Remap(board, corrected, map1, map2, InterpolationFlags.Linear);
Console.WriteLine($"InitUndistortRectifyMap + Remap: {corrected.Size().Width}x{corrected.Size().Height}");

// Video2DPointScanner's threshold + contour pass.
using var thresholded = new Mat();
Cv2.Threshold(gray, thresholded, 128, 255, ThresholdTypes.Binary);
Cv2.FindContours(thresholded, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
Console.WriteLine($"Threshold + FindContours: {contours.Length} contours");
if (contours.Length == 0)
    return Fail("expected at least one contour from the checkerboard");

Console.WriteLine("OPENCV SMOKE OK");
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"OPENCV SMOKE FAILED: {message}");
    return 1;
}
