namespace T3.SystemUi;

/// <summary>A capture mode a camera offers natively. Frames can also be requested in other sizes, at the cost of scaling.</summary>
public readonly record struct CameraMode(int Width, int Height, int Fps);

/// <summary>A connected camera. <see cref="Name"/> is unique among the connected cameras and is what gets saved in a project.</summary>
public sealed record CameraInfo(string Name, IReadOnlyList<CameraMode> Modes);

public enum CameraStreamState
{
    /// <summary>Frames are arriving, or may still arrive while the system asks the user for permission.</summary>
    Open,

    /// <summary>The user or the system refused access to the camera.</summary>
    Denied,

    /// <summary>The camera is gone. Open it again by name once it returns.</summary>
    Disconnected,
}

/// <summary>An open camera delivering RGBA8 frames. After the application shuts cameras down, it reports <see cref="CameraStreamState.Disconnected"/>.</summary>
public interface ICameraStream : IDisposable
{
    CameraStreamState State { get; }

    /// <summary>
    /// Copies the next frame into <paramref name="rgbaPixels"/> as tightly packed rows, growing it if needed.
    /// Returns false without blocking when no new frame is ready.
    /// </summary>
    bool TryReadFrame(ref byte[] rgbaPixels, out int width, out int height);
}

/// <summary>Access to the system's cameras, for operators that can't depend on the windowing backend directly.</summary>
public interface ICameraProvider
{
    /// <summary>Starts camera support on first use. Must be called on the main thread before any other member.</summary>
    void EnsureStarted();

    /// <summary>Lists the connected cameras. Allocates, so don't call it every frame.</summary>
    IReadOnlyList<CameraInfo> GetCameras();

    /// <summary>
    /// Opens a camera by name, or the first one if the name is empty. A zero size or frame rate in
    /// <paramref name="requestedMode"/> leaves that choice to the provider.
    /// </summary>
    ICameraStream? TryOpen(string cameraName, CameraMode requestedMode, out string? error);
}

public static class Cameras
{
    /// <summary>Null where the application has no camera backend.</summary>
    public static ICameraProvider? Provider { get; set; }
}
