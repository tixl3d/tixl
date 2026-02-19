using Mediapipe.Core;
using Mediapipe.PInvoke;

namespace Mediapipe.Framework.Packet;

public class PacketMap : MpResourceHandle
{
    public PacketMap()
    {
        UnsafeNativeMethods.mp_PacketMap__(out IntPtr ptr).Assert();
        Ptr = ptr;
    }

    // TODO: make this constructor internal
    public PacketMap(nint ptr, bool isOwner) : base(ptr, isOwner)
    {
    }

    public int Size => SafeNativeMethods.mp_PacketMap__size(MpPtr);

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.mp_PacketMap__delete(Ptr);
    }

    /// <remarks>
    ///     This method cannot verify that the packet type corresponding to the <paramref name="key" /> is indeed a
    ///     <typeparamref name="T" />,
    ///     so you must make sure by youreself that it is.
    /// </remarks>
    public Packet<T> At<T>(string key)
    {
        UnsafeNativeMethods.mp_PacketMap__find__PKc(MpPtr, key, out IntPtr packetPtr).Assert();

        if (packetPtr == nint.Zero) return default!; // null

        return new Packet<T>(packetPtr, true);
    }

    /// <remarks>
    ///     This method cannot verify that the packet type corresponding to the <paramref name="key" /> is indeed a
    ///     <typeparamref name="T" />,
    ///     so you must make sure by youreself that it is.
    /// </remarks>
    public bool TryGet<T>(string key, out Packet<T> packet)
    {
        UnsafeNativeMethods.mp_PacketMap__find__PKc(MpPtr, key, out IntPtr packetPtr).Assert();

        if (packetPtr == nint.Zero)
        {
            packet = default!; // null
            return false;
        }

        packet = new Packet<T>(packetPtr, true);
        return true;
    }

    public void Emplace<T>(string key, Packet<T> packet)
    {
        UnsafeNativeMethods.mp_PacketMap__emplace__PKc_Rp(MpPtr, key, packet.MpPtr).Assert();
        packet.Dispose(); // respect move semantics
        GC.KeepAlive(this);
    }

    public int Erase(string key)
    {
        UnsafeNativeMethods.mp_PacketMap__erase__PKc(MpPtr, key, out int count).Assert();

        GC.KeepAlive(this);
        return count;
    }

    public void Clear()
    {
        SafeNativeMethods.mp_PacketMap__clear(MpPtr);
    }
}