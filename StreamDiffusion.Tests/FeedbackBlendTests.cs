using System.Reflection;
using System.Runtime.Serialization;
using Xunit;

namespace StreamDiffusion.Tests;

/// <summary>
/// Pins the temporal feedback blend ("Feed prev. output") the operator applies to generated frames:
/// f=0 is a pass-through, f&gt;0 blends the new frame with the last published (blended) one, so trails
/// decay exponentially, and a resolution change resets the history.
/// </summary>
public class FeedbackBlendTests : IDisposable
{
    private readonly MethodInfo _applyFeedback = typeof(Lib.io.ai.StreamDiffusion)
        .GetMethod("ApplyFeedback", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("ApplyFeedback not found");

    /** The blend reads only plain fields; an uninitialized instance carries exactly a fresh op's
     *  history state (null frame, zero size) without needing the editor's construction context. */
    private readonly object _op = FormatterServices.GetUninitializedObject(typeof(Lib.io.ai.StreamDiffusion));

    public void Dispose()
    {
    }

    private byte[] Invoke(byte[] data, int width, int height, float feedback)
        => (byte[])_applyFeedback.Invoke(_op, new object[] { data, width, height, feedback })!;

    [Fact]
    public void ZeroFeedback_IsPassThrough()
    {
        var data = new byte[] { 10, 20, 30, 255, 200, 100, 50, 255 };
        Assert.Equal(data, Invoke(data, 2, 1, 0f));
    }

    [Fact]
    public void HalfFeedback_BlendsWithPreviousFrame()
    {
        Invoke(new byte[] { 100, 100, 100, 255 }, 1, 1, 0.5f); // seeds the history

        var second = Invoke(new byte[] { 200, 200, 200, 255 }, 1, 1, 0.5f);

        // 200 * 0.5 + 100 * 0.5 = 150 (with +0.5 rounding)
        Assert.Equal(150, second[0]);
        Assert.Equal(150, second[1]);
        Assert.Equal(150, second[2]);
        Assert.Equal(255, second[3]);
    }

    [Fact]
    public void HistoryIsTheBlendedFrame_TrailsDecay()
    {
        Invoke(new byte[] { 100, 100, 100, 255 }, 1, 1, 0.5f);

        var flash = Invoke(new byte[] { 255, 255, 255, 255 }, 1, 1, 0.5f); // ~178
        // The blend writes in place into the shared history buffer and returns it, so the flash
        // value must be read before the next call overwrites it with the decayed frame.
        var flashValue = flash[0];
        var after = Invoke(new byte[] { 0, 0, 0, 255 }, 1, 1, 0.5f);      // decays, not zero

        Assert.Equal(89, after[0]);
        Assert.True(flashValue is > 150 and < 200);
    }

    [Fact]
    public void ResolutionChange_ResetsHistory()
    {
        Invoke(new byte[] { 100, 100, 100, 255 }, 1, 1, 0.5f);

        var second = Invoke(new byte[] { 200, 200, 200, 255, 200, 200, 200, 255 }, 2, 1, 0.5f);
        // No history for the new size: pass-through of the new frame
        Assert.Equal(200, second[0]);
        Assert.Equal(200, second[4]);
    }

}
