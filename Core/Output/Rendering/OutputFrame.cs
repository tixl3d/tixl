#nullable enable
namespace T3.Core.Output.Rendering;

/// <summary>
/// Counts the frames of output work. Everything the compositing path memoises — a send's content, a surface's
/// slice, an output's composite — records the frame it was made for, so within one frame the presentation
/// pass, a stream sender and an editor's preview all get the pixels the first of them rendered.
/// <para>The host advances it once per frame, before anything asks for a composite. A host that never does
/// gets the first frame's answers forever, which is the loud kind of wrong.</para>
/// <para>Not thread-safe, and not meant to be: compositing runs on the host's render thread.</para>
/// </summary>
public static class OutputFrame
{
    /// <summary>How many frames of output work have begun; compared for equality, never measured.</summary>
    public static int Index { get; private set; }

    public static void Advance()
    {
        Index++;
    }
}
