using Mediapipe.Extension;
using Mediapipe.TranMarshal;

namespace Mediapipe.Tasks.Components.Containers;

/// <summary>
///     Landmark represents a point in 3D space with x, y, z coordinates. The
///     landmark coordinates are in meters. z represents the landmark depth, and the
///     smaller the value the closer the world landmark is to the camera.
/// </summary>
public readonly struct Landmark : IEquatable<Landmark>
{
    public const float LandmarkTolerance = 1e-6f;

    public readonly float X;
    public readonly float Y;
    public readonly float Z;

    /// <summary>
    ///     Landmark visibility. Should stay unset if not supported.
    ///     Float score of whether landmark is visible or occluded by other objects.
    ///     Landmark considered as invisible also if it is not present on the screen
    ///     (out of scene bounds). Depending on the model, visibility value is either a
    ///     sigmoid or an argument of sigmoid.
    /// </summary>
    public readonly float? Visibility;

    /// <summary>
    ///     Landmark presence. Should stay unset if not supported.
    ///     Float score of whether landmark is present on the scene (located within
    ///     scene bounds). Depending on the model, presence value is either a result of
    ///     sigmoid or an argument of sigmoid function to get landmark presence
    ///     probability.
    /// </summary>
    public readonly float? Presence;

    /// <summary>
    ///     Landmark name. Should stay unset if not supported.
    /// </summary>
    public readonly string? Name;

    internal Landmark(float x, float y, float z, float? visibility, float? presence) : this(x, y, z, visibility,
        presence, null)
    {
    }

    internal Landmark(float x, float y, float z, float? visibility, float? presence, string? name)
    {
        X = x;
        Y = y;
        Z = z;
        Visibility = visibility;
        Presence = presence;
        Name = name;
    }

    internal Landmark(NativeLandmark nativeLandmark) : this(
        nativeLandmark.x, nativeLandmark.y, nativeLandmark.z,
        nativeLandmark.hasVisibility ? nativeLandmark.visibility : null,
        nativeLandmark.hasPresence ? nativeLandmark.presence : null,
        nativeLandmark.Name
    )
    {
    }

    public override bool Equals(object? obj)
    {
        return obj is Landmark other && Equals(other);
    }

    bool IEquatable<Landmark>.Equals(Landmark other)
    {
        return MathF.Abs(X - other.X) < LandmarkTolerance &&
               MathF.Abs(Y - other.Y) < LandmarkTolerance &&
               MathF.Abs(Z - other.Z) < LandmarkTolerance;
    }

    // TODO: use HashCode.Combine
    public override int GetHashCode()
    {
        return Tuple.Create(X, Y, Z).GetHashCode();
    }

    public static bool operator ==(in Landmark lhs, in Landmark rhs)
    {
        return lhs.Equals(rhs);
    }

    public static bool operator !=(in Landmark lhs, in Landmark rhs)
    {
        return !(lhs == rhs);
    }

    public static Landmark CreateFrom(Mediapipe.Landmark proto)
    {
        return new Landmark(
            proto.X, proto.Y, proto.Z,
            proto.HasVisibility ? proto.Visibility : null,
            proto.HasPresence ? proto.Presence : null
        );
    }

    public override string ToString()
    {
        return
            $"{{ \"x\": {X}, \"y\": {Y}, \"z\": {Z}, \"visibility\": {Util.Format(Visibility)}, \"presence\": {Util.Format(Presence)}, \"name\": \"{Name}\" }}";
    }
}

/// <summary>
///     A normalized version of above Landmark struct. All coordinates should be
///     within [0, 1].
/// </summary>
public readonly struct NormalizedLandmark : IEquatable<NormalizedLandmark>
{
    public const float LandmarkTolerance = 1e-6f;

    public readonly float X;
    public readonly float Y;
    public readonly float Z;
    public readonly float? Visibility;
    public readonly float? Presence;
    public readonly string? Name;

    internal NormalizedLandmark(float x, float y, float z, float? visibility, float? presence) : this(x, y, z,
        visibility, presence, null)
    {
    }

    internal NormalizedLandmark(float x, float y, float z, float? visibility, float? presence, string? name)
    {
        X = x;
        Y = y;
        Z = z;
        Visibility = visibility;
        Presence = presence;
        Name = name;
    }

    internal NormalizedLandmark(NativeNormalizedLandmark nativeLandmark) : this(
        nativeLandmark.x, nativeLandmark.y, nativeLandmark.z,
        nativeLandmark.hasVisibility ? nativeLandmark.visibility : null,
        nativeLandmark.hasPresence ? nativeLandmark.presence : null,
        nativeLandmark.Name
    )
    {
    }

    public override bool Equals(object? obj)
    {
        return obj is NormalizedLandmark other && Equals(other);
    }

    bool IEquatable<NormalizedLandmark>.Equals(NormalizedLandmark other)
    {
        return MathF.Abs(X - other.X) < LandmarkTolerance &&
               MathF.Abs(Y - other.Y) < LandmarkTolerance &&
               MathF.Abs(Z - other.Z) < LandmarkTolerance;
    }

    // TODO: use HashCode.Combine
    public override int GetHashCode()
    {
        return Tuple.Create(X, Y, Z).GetHashCode();
    }

    public static bool operator ==(in NormalizedLandmark lhs, in NormalizedLandmark rhs)
    {
        return lhs.Equals(rhs);
    }

    public static bool operator !=(in NormalizedLandmark lhs, in NormalizedLandmark rhs)
    {
        return !(lhs == rhs);
    }

    public static NormalizedLandmark CreateFrom(Mediapipe.NormalizedLandmark proto)
    {
        return new NormalizedLandmark(
            proto.X, proto.Y, proto.Z,
            proto.HasVisibility ? proto.Visibility : null,
            proto.HasPresence ? proto.Presence : null
        );
    }

    public override string ToString()
    {
        return
            $"{{ \"x\": {X}, \"y\": {Y}, \"z\": {Z}, \"visibility\": {Util.Format(Visibility)}, \"presence\": {Util.Format(Presence)}, \"name\": \"{Name}\" }}";
    }
}

/// <summary>
///     A list of Landmarks.
/// </summary>
public readonly struct Landmarks
{
    public readonly List<Landmark> landmarks;

    internal Landmarks(List<Landmark> landmarks)
    {
        this.landmarks = landmarks;
    }

    public static Landmarks Alloc(int capacity)
    {
        return new Landmarks(new List<Landmark>(capacity));
    }

    public static Landmarks CreateFrom(LandmarkList proto)
    {
        Landmarks result = default;

        Copy(proto, ref result);
        return result;
    }

    public static void Copy(LandmarkList source, ref Landmarks destination)
    {
        List<Landmark> landmarks = destination.landmarks ?? new List<Landmark>(source.Landmark.Count);
        landmarks.Clear();
        for (int i = 0; i < source.Landmark.Count; i++) landmarks.Add(Landmark.CreateFrom(source.Landmark[i]));

        destination = new Landmarks(landmarks);
    }

    internal static void Copy(NativeLandmarks source, ref Landmarks destination)
    {
        List<Landmark> landmarks = destination.landmarks ?? new List<Landmark>((int)source.landmarksCount);
        landmarks.Clear();

        foreach (NativeLandmark nativeLandmark in source.AsReadOnlySpan()) landmarks.Add(new Landmark(nativeLandmark));
        destination = new Landmarks(landmarks);
    }

    public override string ToString()
    {
        return $"{{ \"landmarks\": {Util.Format(landmarks)} }}";
    }
}

/// <summary>
///     A list of NormalizedLandmarks.
/// </summary>
public readonly struct NormalizedLandmarks
{
    public readonly List<NormalizedLandmark> landmarks;

    internal NormalizedLandmarks(List<NormalizedLandmark> landmarks)
    {
        this.landmarks = landmarks;
    }

    public static NormalizedLandmarks Alloc(int capacity)
    {
        return new NormalizedLandmarks(new List<NormalizedLandmark>(capacity));
    }

    public static NormalizedLandmarks CreateFrom(NormalizedLandmarkList proto)
    {
        NormalizedLandmarks result = default;

        Copy(proto, ref result);
        return result;
    }

    public static void Copy(NormalizedLandmarkList source, ref NormalizedLandmarks destination)
    {
        List<NormalizedLandmark> landmarks =
            destination.landmarks ?? new List<NormalizedLandmark>(source.Landmark.Count);
        landmarks.Clear();
        for (int i = 0; i < source.Landmark.Count; i++)
            landmarks.Add(NormalizedLandmark.CreateFrom(source.Landmark[i]));

        destination = new NormalizedLandmarks(landmarks);
    }

    internal static void Copy(NativeNormalizedLandmarks source, ref NormalizedLandmarks destination)
    {
        List<NormalizedLandmark> landmarks =
            destination.landmarks ?? new List<NormalizedLandmark>((int)source.landmarksCount);
        landmarks.Clear();

        foreach (NativeNormalizedLandmark nativeLandmark in source.AsReadOnlySpan())
            landmarks.Add(new NormalizedLandmark(nativeLandmark));
        destination = new NormalizedLandmarks(landmarks);
    }

    public override string ToString()
    {
        return $"{{ \"landmarks\": {Util.Format(landmarks)} }}";
    }
}

internal static class NativeLandmarksArrayExtension
{
    public static void FillWith(this List<ClassificationResult> target, NativeClassificationResultArray source)
    {
        target.ResizeTo(source.size);

        int i = 0;
        foreach (NativeClassificationResult nativeClassificationResult in source.AsReadOnlySpan())
        {
            ClassificationResult classificationResult = target[i];
            ClassificationResult.Copy(nativeClassificationResult, ref classificationResult);
            target[i++] = classificationResult;
        }
    }

    public static void FillWith(this List<Landmarks> target, NativeLandmarksArray source)
    {
        target.ResizeTo(source.size);

        int i = 0;
        foreach (NativeLandmarks nativeLandmarks in source.AsReadOnlySpan())
        {
            Landmarks landmarks = target[i];
            Landmarks.Copy(nativeLandmarks, ref landmarks);
            target[i++] = landmarks;
        }
    }

    public static void FillWith(this List<NormalizedLandmarks> target, NativeNormalizedLandmarksArray source)
    {
        target.ResizeTo(source.size);

        int i = 0;
        foreach (NativeNormalizedLandmarks nativeLandmarks in source.AsReadOnlySpan())
        {
            NormalizedLandmarks landmarks = target[i];
            NormalizedLandmarks.Copy(nativeLandmarks, ref landmarks);
            target[i++] = landmarks;
        }
    }
}