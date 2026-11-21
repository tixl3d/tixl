#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using OpenCvSharp;

namespace Lib.io.video.depthanything.Utils;

/// <summary>
/// Camera rotation from the image for a camera that only pans and tilts, or sits on a tripod. Depth is not used, so
/// relative-depth wobble cannot disturb it.
/// <para>ORB features are matched against the last few keyframes; a RANSAC homography keeps the matches that agree with one
/// rotation and the rotation is fitted between their viewing rays. Where there are no features (a bare ceiling or wall, a
/// blurred fast move) an intensity-based alignment (ECC) of a quarter-size image takes over, started from the predicted
/// rotation. If both fail the pose follows the last angular velocity for a short while instead of freezing, and the next
/// match snaps it back.</para>
/// The pose composes like <see cref="DepthPoseTracker"/>'s: <c>world = keyframe * delta</c>, <c>delta</c> mapping
/// current-camera rays into the keyframe camera, in the OpenCV camera frame (x right, y down, +Z forward).
/// </summary>
internal sealed class ImageRotationTracker : IDisposable
{
    private const int FeatureCount = 1500;
    private const float RatioTest = 0.75f;
    private const int MinMatches = 20;
    private const int MinInliers = 15;
    private const double RansacPixels = 3.0;
    private const int KeyframeCount = 4;

    /** A keyframe is replaced once the camera turned this far from it: further on, too little of the view is shared. */
    private const float KeyframeAngleDeg = 14f;
    private const int KeyframeInliers = 40;

    private const double SmallScale = 0.25;
    private const double MinEccCorrelation = 0.6;
    private const int MaxPredictedFrames = 6;

    /** Views kept over the whole sweep: a lost tracker re-finds itself in them, and a camera that comes back to an old view corrects its drift. */
    private const int GlobalKeyframeCap = 90;
    private const int GlobalMinInliers = 35;
    private const int LoopCheckEvery = 12;
    private const int RelocalizeAfterLostFrames = 2;

    private sealed class Keyframe : IDisposable
    {
        public KeyPoint[] Points = [];
        public Mat Descriptors = new();
        public Mat Small = new();
        public Quaternion World;

        public void Dispose()
        {
            Descriptors.Dispose();
            Small.Dispose();
        }
    }

    private readonly ORB _orb = ORB.Create(FeatureCount);
    private readonly BFMatcher _matcher = new(NormTypes.Hamming);
    private readonly List<Keyframe> _keys = [];
    private readonly List<Keyframe> _global = [];
    private int _sinceLoopCheck;
    private int _lostFrames;
    private Quaternion _velocity = Quaternion.Identity;
    private bool _hasVelocity;
    private int _predictedFrames;

    public Quaternion Rotation { get; private set; } = Quaternion.Identity;
    public bool HasPose { get; private set; }
    public bool Tracking { get; private set; }
    public int LastInliers { get; private set; }
    public string LastSource { get; private set; } = "none";

    public void Reset()
    {
        foreach (var key in _keys)
            key.Dispose();

        _keys.Clear();
        foreach (var key in _global)
            key.Dispose();

        _global.Clear();
        _sinceLoopCheck = 0;
        _lostFrames = 0;
        Rotation = Quaternion.Identity;
        _velocity = Quaternion.Identity;
        _hasVelocity = false;
        _predictedFrames = 0;
        HasPose = false;
        Tracking = false;
        LastInliers = 0;
        LastSource = "none";
    }

    /** Feeds one grayscale frame (width x height bytes) with the camera matrix of that pixel grid and updates the world rotation. */
    public void Update(byte[] gray, int width, int height, float fx, float fy, float cx, float cy)
    {
        using var image = Mat.FromPixelData(height, width, MatType.CV_8UC1, gray);
        using var descriptors = new Mat();
        _orb.DetectAndCompute(image, null, out var points, descriptors);
        using var small = new Mat();
        Cv2.Resize(image, small, new Size(0, 0), SmallScale, SmallScale, InterpolationFlags.Area);

        if (_keys.Count == 0)
        {
            AddKeyframe(points, descriptors, small);
            HasPose = true;
            Tracking = true;
            LastSource = "start";
            return;
        }

        // 1) features against the last few keyframes: the one that shares the most with this view wins
        Keyframe? bestKey = null;
        var bestDelta = Quaternion.Identity;
        var bestInliers = 0;
        if (points.Length >= MinMatches && !descriptors.Empty())
        {
            foreach (var key in _keys)
            {
                if (key.Descriptors.Empty() || !MatchFeatures(points, descriptors, key, fx, fy, cx, cy, out var delta, out var inliers) || inliers <= bestInliers)
                    continue;

                bestKey = key;
                bestDelta = delta;
                bestInliers = inliers;
            }
        }

        var source = "orb";
        LastInliers = bestInliers;

        // 2) no features to go on: align the image intensities, starting from where the rotation should be
        if (bestKey == null)
        {
            var key = _keys[^1];
            var predicted = _hasVelocity && _predictedFrames < MaxPredictedFrames ? Rotation * _velocity : Rotation;
            var predictedDelta = Quaternion.Normalize(Quaternion.Inverse(key.World) * predicted);
            if (AlignIntensities(small, key, predictedDelta, fx, fy, cx, cy, out var delta))
            {
                bestKey = key;
                bestDelta = delta;
                source = "ecc";
            }
        }

        // 3) still nothing: look the view up among every keyframe of the sweep
        if (bestKey == null && _lostFrames >= RelocalizeAfterLostFrames && points.Length >= MinMatches && !descriptors.Empty()
            && FindInGlobal(points, descriptors, fx, fy, cx, cy, out var globalKey, out var globalDelta, out var globalInliers))
        {
            bestKey = globalKey;
            bestDelta = globalDelta;
            bestInliers = globalInliers;
            source = "relocalized";
        }

        // 4) lost: follow the last angular velocity for a few frames rather than freeze
        if (bestKey == null)
        {
            _lostFrames++;
            Tracking = false;
            LastSource = "lost";
            if (_hasVelocity && _predictedFrames < MaxPredictedFrames)
            {
                Rotation = Quaternion.Normalize(Rotation * _velocity);
                _predictedFrames++;
                LastSource = "predicted";
            }

            return;
        }

        _lostFrames = 0;
        var world = Quaternion.Normalize(bestKey.World * bestDelta);
        _velocity = source == "relocalized" ? Quaternion.Identity : Quaternion.Normalize(Quaternion.Inverse(Rotation) * world);
        _hasVelocity = source != "relocalized";
        _predictedFrames = 0;
        Rotation = world;
        HasPose = true;
        Tracking = true;
        LastSource = source;

        var turned = 2f * MathF.Acos(Math.Clamp(MathF.Abs(bestDelta.W), 0f, 1f)) * 180f / MathF.PI;

        // Coming back to a view seen earlier in the sweep pulls the accumulated drift back
        if (source == "orb" && ++_sinceLoopCheck >= LoopCheckEvery && points.Length >= MinMatches && !descriptors.Empty())
        {
            _sinceLoopCheck = 0;
            if (FindInGlobal(points, descriptors, fx, fy, cx, cy, out var loopKey, out var loopDelta, out _))
            {
                var corrected = Quaternion.Normalize(loopKey.World * loopDelta);
                if (AngleBetween(corrected, Rotation) > 0.5f)
                {
                    Rotation = corrected;
                    LastSource = "loop";
                }
            }
        }

        if (turned > KeyframeAngleDeg || source == "ecc" || bestInliers < KeyframeInliers)
            AddKeyframe(points, descriptors, small, source == "orb" || source == "relocalized");
    }

    private bool MatchFeatures(KeyPoint[] points, Mat descriptors, Keyframe key, float fx, float fy, float cx, float cy, out Quaternion delta, out int inliers)
    {
        delta = Quaternion.Identity;
        inliers = 0;
        var matches = _matcher.KnnMatch(descriptors, key.Descriptors, 2);
        var current = new List<Point2f>();
        var keyPoints = new List<Point2f>();
        foreach (var pair in matches)
        {
            if (pair.Length < 2 || pair[0].Distance >= RatioTest * pair[1].Distance)
                continue;

            current.Add(points[pair[0].QueryIdx].Pt);
            keyPoints.Add(key.Points[pair[0].TrainIdx].Pt);
        }

        if (current.Count < MinMatches)
            return false;

        using var currentMat = Mat.FromArray(current.ToArray());
        using var keyMat = Mat.FromArray(keyPoints.ToArray());
        using var mask = new Mat();
        using var homography = Cv2.FindHomography(currentMat, keyMat, HomographyMethods.Ransac, RansacPixels, mask);
        if (homography.Empty())
            return false;

        mask.GetArray(out byte[] inlierMask);
        var src = new List<Vector3>();
        var dst = new List<Vector3>();
        var weight = new List<float>();
        for (var i = 0; i < inlierMask.Length; i++)
        {
            if (inlierMask[i] == 0)
                continue;

            src.Add(Ray(current[i], fx, fy, cx, cy));
            dst.Add(Ray(keyPoints[i], fx, fy, cx, cy));
            weight.Add(1f);
        }

        if (src.Count < MinInliers || !Sim3Fit.Fit(src, dst, weight, out var fit))
            return false;

        delta = fit.Rotation;
        inliers = src.Count;
        return true;
    }

    /** ECC on quarter-size images: finds the homography between the keyframe and this view and reads the rotation out of it. */
    private static bool AlignIntensities(Mat small, Keyframe key, Quaternion predictedDelta, float fx, float fy, float cx, float cy, out Quaternion delta)
    {
        delta = Quaternion.Identity;
        if (key.Small.Empty() || key.Small.Size() != small.Size())
            return false;

        // Camera matrix of the small grid; for a rotation R mapping current to keyframe rays, key pixels reach current pixels by K R^T K^-1
        var ks = new Matrix4x4((float)(fx * SmallScale), 0, (float)(cx * SmallScale), 0,
                               0, (float)(fy * SmallScale), (float)(cy * SmallScale), 0,
                               0, 0, 1, 0,
                               0, 0, 0, 1);
        if (!Matrix4x4.Invert(ks, out var ksInverse))
            return false;

        // Matrix4x4.CreateFromQuaternion is laid out for row vectors, which is the transpose of the column rotation: R^T
        var rt = Matrix4x4.CreateFromQuaternion(predictedDelta);
        var start = ColumnProduct(ks, rt, ksInverse);
        using var warp = new Mat(3, 3, MatType.CV_32FC1);
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
                warp.Set(r, c, (float)(start[r, c] / start[2, 2]));
        }

        double correlation;
        try
        {
            correlation = Cv2.FindTransformECC(key.Small, small, warp, MotionTypes.Homography,
                                               new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.Count, 60, 1e-5), null, 5);
        }
        catch (OpenCVException)
        {
            return false;
        }

        if (correlation < MinEccCorrelation)
            return false;

        // K^-1 W K is R^T up to scale (key to current); normalise the scale and read the rotation
        var w = new double[3, 3];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
                w[r, c] = warp.Get<float>(r, c);
        }

        var rotated = ColumnProduct(ksInverse, w, ks);
        var det = rotated[0, 0] * (rotated[1, 1] * rotated[2, 2] - rotated[1, 2] * rotated[2, 1])
                  - rotated[0, 1] * (rotated[1, 0] * rotated[2, 2] - rotated[1, 2] * rotated[2, 0])
                  + rotated[0, 2] * (rotated[1, 0] * rotated[2, 1] - rotated[1, 1] * rotated[2, 0]);
        if (!(det > 1e-9))
            return false;

        var scale = (float)Math.Cbrt(det);
        var m = new Matrix4x4((float)(rotated[0, 0] / scale), (float)(rotated[0, 1] / scale), (float)(rotated[0, 2] / scale), 0,
                              (float)(rotated[1, 0] / scale), (float)(rotated[1, 1] / scale), (float)(rotated[1, 2] / scale), 0,
                              (float)(rotated[2, 0] / scale), (float)(rotated[2, 1] / scale), (float)(rotated[2, 2] / scale), 0,
                              0, 0, 0, 1);

        // The matrix just read out is R^T, exactly the row-vector layout CreateFromRotationMatrix expects for the rotation R
        delta = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
        return float.IsFinite(delta.W);
    }

    private static double[,] ColumnProduct(Matrix4x4 a, Matrix4x4 b, Matrix4x4 c)
    {
        var x = Mul(ToArray(a), ToArray(b));
        return Mul(x, ToArray(c));
    }

    private static double[,] ColumnProduct(Matrix4x4 a, double[,] b, Matrix4x4 c) => Mul(Mul(ToArray(a), b), ToArray(c));

    private static double[,] ToArray(Matrix4x4 m) => new double[,]
    {
        { m.M11, m.M12, m.M13 },
        { m.M21, m.M22, m.M23 },
        { m.M31, m.M32, m.M33 },
    };

    private static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[3, 3];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
                r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
        }

        return r;
    }

    private static Vector3 Ray(Point2f p, float fx, float fy, float cx, float cy) =>
        Vector3.Normalize(new Vector3((p.X - cx) / fx, (p.Y - cy) / fy, 1f));

    private static float AngleBetween(Quaternion a, Quaternion b) => 2f * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0f, 1f)) * 180f / MathF.PI;

    private bool FindInGlobal(KeyPoint[] points, Mat descriptors, float fx, float fy, float cx, float cy, out Keyframe? best, out Quaternion bestDelta, out int bestInliers)
    {
        best = null;
        bestDelta = Quaternion.Identity;
        bestInliers = 0;
        foreach (var key in _global)
        {
            if (_keys.Contains(key) || key.Descriptors.Empty() || !MatchFeatures(points, descriptors, key, fx, fy, cx, cy, out var delta, out var inliers) || inliers <= bestInliers)
                continue;

            best = key;
            bestDelta = delta;
            bestInliers = inliers;
        }

        return best != null && bestInliers >= GlobalMinInliers;
    }

    private void AddKeyframe(KeyPoint[] points, Mat descriptors, Mat small, bool keepGlobally = true)
    {
        var added = new Keyframe { Points = points, Descriptors = descriptors.Clone(), Small = small.Clone(), World = Rotation };
        if (keepGlobally && _global.Count < GlobalKeyframeCap && (_global.Count == 0 || AngleBetween(_global[^1].World, Rotation) > 8f))
            _global.Add(added);

        _keys.Add(added);
        while (_keys.Count > KeyframeCount)
        {
            if (!_global.Contains(_keys[0]))
                _keys[0].Dispose();

            _keys.RemoveAt(0);
        }
    }

    public void Dispose()
    {
        Reset();
        _orb.Dispose();
        _matcher.Dispose();
    }
}
