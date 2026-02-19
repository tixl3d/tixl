using System.Runtime.InteropServices;
using Mediapipe.Framework.Port;
using Mediapipe.PInvoke;

namespace Mediapipe.Core;

public abstract class MpResourceHandle : DisposableObject
{
    private nint _ptr = nint.Zero;

    protected MpResourceHandle(bool isOwner = true) : this(nint.Zero, isOwner)
    {
    }

    protected MpResourceHandle(nint ptr, bool isOwner = true) : base(isOwner)
    {
        Ptr = ptr;
    }

    protected nint Ptr
    {
        get => _ptr;
        set
        {
            if (value != nint.Zero && OwnsResource())
                throw new InvalidOperationException("This object owns another resource");
            _ptr = value;
        }
    }

    public nint MpPtr
    {
        get
        {
            ThrowIfDisposed();
            return Ptr;
        }
    }

    /// <summary>
    ///     Relinquish the ownership, and release the resource it owns if necessary.
    ///     This method should be called only if the underlying native api moves the pointer.
    /// </summary>
    /// <remarks>If the object itself is no longer used, call <see cref="Dispose" /> instead.</remarks>
    internal void ReleaseMpResource()
    {
        if (OwnsResource()) DeleteMpPtr();
        ReleaseMpPtr();
        TransferOwnership();
    }

    public bool OwnsResource()
    {
        return IsOwner && IsResourcePresent();
    }

    protected override void DisposeUnmanaged()
    {
        if (OwnsResource()) DeleteMpPtr();
        ReleaseMpPtr();
        base.DisposeUnmanaged();
    }

    /// <summary>
    ///     Forgets the pointer address.
    ///     After calling this method, <see ref="OwnsResource" /> will return false.
    /// </summary>
    protected void ReleaseMpPtr()
    {
        Ptr = nint.Zero;
    }

    /// <summary>
    ///     Release the memory (call `delete` or `delete[]`) whether or not it owns it.
    /// </summary>
    /// <remarks>In most cases, this method should not be called directly</remarks>
    protected abstract void DeleteMpPtr();

    protected string? MarshalStringFromNative(StringOutFunc f)
    {
        f(MpPtr, out IntPtr strPtr).Assert();

        return MarshalStringFromNative(strPtr);
    }

    protected static string? MarshalStringFromNative(nint strPtr)
    {
        string? str = Marshal.PtrToStringAnsi(strPtr);
        UnsafeNativeMethods.delete_array__PKc(strPtr);

        return str;
    }

    /// <summary>
    ///     The optimized implementation of <see cref="Status.AssertOk" />.
    /// </summary>
    protected static void AssertStatusOk(nint statusPtr)
    {
        bool ok = SafeNativeMethods.absl_Status__ok(statusPtr);
        if (!ok)
        {
            using Status status = new(statusPtr);
            status.AssertOk();
        }
        else
        {
            UnsafeNativeMethods.absl_Status__delete(statusPtr);
        }
    }

    protected bool IsResourcePresent()
    {
        return Ptr != nint.Zero;
    }

    protected delegate MpReturnCode StringOutFunc(nint ptr, out nint strPtr);
}