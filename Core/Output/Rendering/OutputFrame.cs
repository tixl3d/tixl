#nullable enable
namespace T3.Core.Output.Rendering;

/// <summary>
/// Marks off one frame of output work. Everything the compositing path memoises — a send's content, a
/// surface's slice, an output's composite — is keyed on <see cref="Token"/>, so within a frame the
/// presentation pass, a stream sender and an editor's preview all get the pixels the first of them rendered.
/// <para>The host advances it once per frame, before anything asks for a composite. A host that never does
/// gets the first frame's answers forever, which is the loud kind of wrong.</para>
/// </summary>
public static class OutputFrame
{
    /// <summary>The current frame's identity; never meaningful as a count, only as "same frame or not".</summary>
    public static int Token { get; private set; }

    public static void Advance()
    {
        Token++;
    }
}
