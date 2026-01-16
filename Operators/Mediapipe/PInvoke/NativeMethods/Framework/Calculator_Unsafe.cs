using System.Runtime.InteropServices;
using Mediapipe.External;

namespace Mediapipe.PInvoke;

internal static partial class UnsafeNativeMethods
{
    [DllImport(LibName.MediaPipeLibrary, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool mp_api__ConvertFromCalculatorGraphConfigTextFormat(string configText,
        out SerializedProto serializedProto);
}