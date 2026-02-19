using Mediapipe.Core;
using Mediapipe.PInvoke;

namespace Mediapipe.Framework;

public class OutputStreamPoller<T>(nint ptr) : MpResourceHandle(ptr)
{
    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.mp_OutputStreamPoller__delete(Ptr);
    }

    public bool Next(Packet<T> packet)
    {
        UnsafeNativeMethods.mp_OutputStreamPoller__Next_Ppacket(MpPtr, packet.MpPtr, out bool result).Assert();

        return result;
    }

    public void Reset()
    {
        UnsafeNativeMethods.mp_OutputStreamPoller__Reset(MpPtr).Assert();
    }

    public void SetMaxQueueSize(int queueSize)
    {
        UnsafeNativeMethods.mp_OutputStreamPoller__SetMaxQueueSize(MpPtr, queueSize).Assert();
    }

    public int QueueSize()
    {
        UnsafeNativeMethods.mp_OutputStreamPoller__QueueSize(MpPtr, out int result).Assert();

        return result;
    }
}