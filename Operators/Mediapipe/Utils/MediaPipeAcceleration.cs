namespace Lib.io.video.mediapipe;

/// <summary>Inference acceleration for the mediapipe tasks.
/// <para><c>Cpu</c> is the plain single-threaded TF Lite CPU path (works everywhere).</para>
/// <para><c>Xnnpack</c> is the multithreaded CPU path - usually several times faster, available in
/// every native build, and tunable with the operators' InferenceThreads input.</para>
/// <para><c>Gpu</c> uses the OpenGL/EGL delegate: available on the Linux native, falls back to CPU
/// with a logged warning on the CPU-only Windows/macOS builds.</para></summary>
public enum MediaPipeAcceleration
{
    Cpu,
    Xnnpack,
    Gpu,
}
