using System.Runtime.InteropServices;
using Mediapipe.Core;
using Mediapipe.PInvoke;

namespace Mediapipe.Framework.Port;

public enum StatusCode
{
    Ok = 0,
    Cancelled = 1,
    Unknown = 2,
    InvalidArgument = 3,
    DeadlineExceeded = 4,
    NotFound = 5,
    AlreadyExists = 6,
    PermissionDenied = 7,
    ResourceExhausted = 8,
    FailedPrecondition = 9,
    Aborted = 10,
    OutOfRange = 11,
    Unimplemented = 12,
    Internal = 13,
    Unavailable = 14,
    DataLoss = 15,
    Unauthenticated = 16
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct StatusArgs
{
    private readonly StatusCode _code;
    private readonly nint _message;

    private StatusArgs(StatusCode code, string? message = null)
    {
        _code = code;
        _message = Marshal.StringToHGlobalAnsi(message);
    }

    public static StatusArgs Ok()
    {
        return new StatusArgs(StatusCode.Ok);
    }

    public static StatusArgs Cancelled(string? message = null)
    {
        return new StatusArgs(StatusCode.Cancelled, message);
    }

    public static StatusArgs Unknown(string? message = null)
    {
        return new StatusArgs(StatusCode.Unknown, message);
    }

    public static StatusArgs InvalidArgument(string? message = null)
    {
        return new StatusArgs(StatusCode.InvalidArgument, message);
    }

    public static StatusArgs DeadlineExceeded(string? message = null)
    {
        return new StatusArgs(StatusCode.DeadlineExceeded, message);
    }

    public static StatusArgs NotFound(string? message = null)
    {
        return new StatusArgs(StatusCode.NotFound, message);
    }

    public static StatusArgs AlreadyExists(string? message = null)
    {
        return new StatusArgs(StatusCode.AlreadyExists, message);
    }

    public static StatusArgs PermissionDenied(string? message = null)
    {
        return new StatusArgs(StatusCode.PermissionDenied, message);
    }

    public static StatusArgs ResourceExhausted(string? message = null)
    {
        return new StatusArgs(StatusCode.ResourceExhausted, message);
    }

    public static StatusArgs FailedPrecondition(string? message = null)
    {
        return new StatusArgs(StatusCode.FailedPrecondition, message);
    }

    public static StatusArgs Aborted(string? message = null)
    {
        return new StatusArgs(StatusCode.Aborted, message);
    }

    public static StatusArgs OutOfRange(string? message = null)
    {
        return new StatusArgs(StatusCode.OutOfRange, message);
    }

    public static StatusArgs Unimplemented(string? message = null)
    {
        return new StatusArgs(StatusCode.Unimplemented, message);
    }

    public static StatusArgs Internal(string? message = null)
    {
        return new StatusArgs(StatusCode.Internal, message);
    }

    public static StatusArgs Unavailable(string? message = null)
    {
        return new StatusArgs(StatusCode.Unavailable, message);
    }

    public static StatusArgs DataLoss(string? message = null)
    {
        return new StatusArgs(StatusCode.DataLoss, message);
    }

    public static StatusArgs Unauthenticated(string? message = null)
    {
        return new StatusArgs(StatusCode.Unauthenticated, message);
    }
}

internal class Status(nint ptr, bool isOwner = true) : MpResourceHandle(ptr, isOwner)
{
    private bool? _ok;
    private int? _rawCode;

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.absl_Status__delete(Ptr);
    }

    /// <summary>
    ///     The optimized implementation of <see cref="AssertOk" />.
    /// </summary>
    public static void UnsafeAssertOk(nint statusPtr)
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

    public void AssertOk()
    {
        if (!Ok()) throw new BadStatusException(Code(), ToString());
    }

    public bool Ok()
    {
        if (_ok is bool valueOfOk) return valueOfOk;
        _ok = SafeNativeMethods.absl_Status__ok(MpPtr);
        return (bool)_ok;
    }

    public StatusCode Code()
    {
        return (StatusCode)RawCode();
    }

    public int RawCode()
    {
        if (_rawCode is int valueOfRawCode) return valueOfRawCode;
        _rawCode = SafeNativeMethods.absl_Status__raw_code(MpPtr);
        return (int)_rawCode;
    }

    public override string ToString()
    {
        return MarshalStringFromNative(UnsafeNativeMethods.absl_Status__ToString) ?? "";
    }

    public static Status Build(StatusCode code, string message, bool isOwner = true)
    {
        UnsafeNativeMethods.absl_Status__i_PKc((int)code, message, out IntPtr ptr).Assert();

        return new Status(ptr, isOwner);
    }

    public static Status Ok(bool isOwner = true)
    {
        return Build(StatusCode.Ok, "", isOwner);
    }

    public static Status Cancelled(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.Cancelled, message, isOwner);
    }

    public static Status Unknown(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.Unknown, message, isOwner);
    }

    public static Status InvalidArgument(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.InvalidArgument, message, isOwner);
    }

    public static Status DeadlineExceeded(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.DeadlineExceeded, message, isOwner);
    }

    public static Status NotFound(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.NotFound, message, isOwner);
    }

    public static Status AlreadyExists(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.AlreadyExists, message, isOwner);
    }

    public static Status PermissionDenied(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.PermissionDenied, message, isOwner);
    }

    public static Status ResourceExhausted(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.ResourceExhausted, message, isOwner);
    }

    public static Status FailedPrecondition(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.FailedPrecondition, message, isOwner);
    }

    public static Status Aborted(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.Aborted, message, isOwner);
    }

    public static Status OutOfRange(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.OutOfRange, message, isOwner);
    }

    public static Status Unimplemented(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.Unimplemented, message, isOwner);
    }

    public static Status Internal(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.Internal, message, isOwner);
    }

    public static Status Unavailable(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.Unavailable, message, isOwner);
    }

    public static Status DataLoss(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.DataLoss, message, isOwner);
    }

    public static Status Unauthenticated(string message = "", bool isOwner = true)
    {
        return Build(StatusCode.Unauthenticated, message, isOwner);
    }
}