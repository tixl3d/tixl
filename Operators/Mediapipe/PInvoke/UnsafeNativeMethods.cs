using System.Runtime.InteropServices;
using System.Security;

namespace Mediapipe.PInvoke;

[SuppressUnmanagedCodeSecurity]
internal static partial class UnsafeNativeMethods
{
    static UnsafeNativeMethods()
    {
        mp_api__SetFreeHGlobal(FreeHGlobal);
    }

    private static void FreeHGlobal(IntPtr hglobal)
    {
        Marshal.FreeHGlobal(hglobal);
    }

    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    private static extern void mp_api__SetFreeHGlobal(
        [MarshalAs(UnmanagedType.FunctionPtr)] FreeHGlobalDelegate freeHGlobal);

    private delegate void FreeHGlobalDelegate(IntPtr hglobal);
}