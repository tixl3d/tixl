namespace Mediapipe.PInvoke;

public static class LibName
{
    // Bare library name: the .NET native resolver appends the platform's prefix/suffix, so this resolves
    // to mediapipe_c.dll (Windows), libmediapipe_c.so (Linux) and libmediapipe_c.dylib (macOS) - all three
    // ship flat in the application directory (see Dependencies/linux-x64 and Dependencies/osx).
    public const string MediaPipeLibrary = "mediapipe_c";
}