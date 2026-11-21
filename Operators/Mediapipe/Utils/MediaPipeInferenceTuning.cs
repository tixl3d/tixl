namespace Lib.io.video.mediapipe;

/// <summary>
/// Shared tuning for the inference workers.
/// <para>
/// The default for the Xnnpack delegate is 4 threads. Passing 0 - the "library default" the binding
/// exposes - leaves XNNPACK effectively single-threaded, which measured 2.3x slower than 4 on a 20-core
/// machine (hand_landmarker at 320x240: 20.7 ms/frame at 0, 9.1 ms at 4). Beyond 4 the curve flattens
/// (8 threads: 8.1 ms), so 4 is the elbow.
/// </para>
/// <para>
/// This is a default for a single operator in a typical graph, not a global pool size: seven operators
/// each holding 4 busy threads on one frame would oversubscribe the machine. Graphs that drive several
/// MediaPipe operators from the same camera should lower <c>InferenceThreads</c> on each of them (2 is a
/// good starting point) - the thread count is an ordinary input precisely so that trade-off stays in the
/// graph's hands rather than baked into the operator.
/// </para>
/// </summary>
internal static class MediaPipeInferenceTuning
{
    /// <summary>Threads handed to the Xnnpack delegate when the operator's input is left at its default.</summary>
    internal const int DefaultThreads = 4;
}
