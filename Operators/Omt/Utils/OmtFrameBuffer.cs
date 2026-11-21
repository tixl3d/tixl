#nullable enable
namespace Lib.Utils;

/// <summary>
/// One video frame in native memory, handed between the render thread and an OMT session thread. Native rather than
/// a managed array, so a 4K frame never lands on the large object heap and needs no pinning for the codec.
/// <para>Not thread-safe by itself: whoever holds a buffer owns it, and buffers shared through a session only change
/// hands under that session's lock.</para>
/// </summary>
internal sealed class OmtFrameBuffer
{
    private OmtFrameBuffer(IntPtr data, int capacity)
    {
        Data = data;
        Capacity = capacity;
    }

    /** Null when the memory isn't available; callers skip the frame. */
    public static OmtFrameBuffer? TryAllocate(int capacity)
    {
        try
        {
            unsafe
            {
                return new OmtFrameBuffer((IntPtr)NativeMemory.AlignedAlloc((nuint)capacity, Alignment), capacity);
            }
        }
        catch (OutOfMemoryException)
        {
            return null;
        }
    }

    public IntPtr Data { get; private set; }
    public readonly int Capacity;

    public int Width;
    public int Height;
    public int Stride;

    /** Bytes the codec reads: whole rows. */
    public int Length;

    // Sender only: how the frame is encoded and announced.
    public libomtnet.OMTCodec Codec;
    public int FrameRate;
    public long Timestamp;
    public bool HasAlpha;
    public bool SwapRedAndBlue;

    public void Free()
    {
        if (Data == IntPtr.Zero)
            return;

        unsafe
        {
            NativeMemory.AlignedFree((void*)Data);
        }

        Data = IntPtr.Zero;
    }

    private const nuint Alignment = 64;
}
