using System.Runtime.InteropServices;
using T3.Core.Logging;

namespace T3.SdlPlatform;

/// <summary>
/// Asks macOS for camera access. SDL only reads the authorization state, and opening a camera while it is still
/// undetermined fails with "Cannot create AVCaptureDeviceInput" without ever showing the system prompt.
/// </summary>
internal static unsafe class MacCameraPermission
{
    public enum State
    {
        Authorized,
        Pending,
        Denied,
    }

    /// <summary>Shows the system prompt on the first call that finds the access undetermined.</summary>
    public static State Check()
    {
        if (!TryInitialize())
            return State.Authorized; // Let SDL try and report its own error

        var status = MsgSendStatus(_captureDeviceClass, _authorizationStatusSelector, _mediaTypeVideo);
        switch (status)
        {
            case AuthorizationStatusAuthorized:
                return State.Authorized;

            case AuthorizationStatusNotDetermined:
                if (Interlocked.Exchange(ref _hasRequested, 1) == 0)
                    MsgSendRequest(_captureDeviceClass, _requestAccessSelector, _mediaTypeVideo, (IntPtr)_completionBlock);

                return State.Pending;

            default:
                return State.Denied;
        }
    }

    private static bool TryInitialize()
    {
        if (_isInitialized)
            return _completionBlock != null;

        _isInitialized = true;
        if (!NativeLibrary.TryLoad(AvFoundationLibrary, out var avFoundation)
            || !NativeLibrary.TryGetExport(avFoundation, "AVMediaTypeVideo", out var mediaTypeExport)
            || !NativeLibrary.TryLoad(SystemLibrary, out var system)
            || !NativeLibrary.TryGetExport(system, "_NSConcreteGlobalBlock", out var globalBlockClass))
        {
            Log.Warning("Could not load AVFoundation to request camera access.");
            return false;
        }

        _mediaTypeVideo = *(IntPtr*)mediaTypeExport;
        _captureDeviceClass = objc_getClass("AVCaptureDevice");
        _authorizationStatusSelector = sel_registerName("authorizationStatusForMediaType:");
        _requestAccessSelector = sel_registerName("requestAccessForMediaType:completionHandler:");

        // A global block is never copied or freed by the runtime, so it can live in unmanaged memory for good.
        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (ulong)sizeof(BlockLiteral);

        var block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        block->Isa = globalBlockClass;
        block->Flags = BlockIsGlobal;
        block->Invoke = (IntPtr)(delegate* unmanaged<IntPtr, byte, void>)&OnAccessAnswered;
        block->Descriptor = descriptor;
        _completionBlock = block;
        return true;
    }

    [UnmanagedCallersOnly]
    private static void OnAccessAnswered(IntPtr block, byte isGranted)
    {
        Log.Debug(isGranted != 0 ? "Camera access granted." : "Camera access denied.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public BlockDescriptor* Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public ulong Reserved;
        public ulong Size;
    }

    private const string ObjCLibrary = "/usr/lib/libobjc.A.dylib";
    private const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
    private const string AvFoundationLibrary = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";

    private const int BlockIsGlobal = 1 << 28;
    private const long AuthorizationStatusNotDetermined = 0;
    private const long AuthorizationStatusAuthorized = 3;

    [DllImport(ObjCLibrary)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCLibrary)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern long MsgSendStatus(IntPtr receiver, IntPtr selector, IntPtr mediaType);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void MsgSendRequest(IntPtr receiver, IntPtr selector, IntPtr mediaType, IntPtr completionHandler);

    private static bool _isInitialized;
    private static int _hasRequested;
    private static IntPtr _mediaTypeVideo;
    private static IntPtr _captureDeviceClass;
    private static IntPtr _authorizationStatusSelector;
    private static IntPtr _requestAccessSelector;
    private static BlockLiteral* _completionBlock;
}
