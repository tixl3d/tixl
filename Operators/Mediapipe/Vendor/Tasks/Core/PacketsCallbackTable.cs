using Mediapipe.Framework.Packet;
using Mediapipe.Framework.Port;
using Mediapipe.Utils;

namespace Mediapipe.Tasks.Core;

internal class PacketsCallbackTable
{
    private const int _MaxSize = 20;

    private static int _Counter;
    private static readonly GlobalInstanceTable<int, TaskRunner.PacketsCallback> _Table = new(_MaxSize);

    public static (int, TaskRunner.NativePacketsCallback?) Add(TaskRunner.PacketsCallback? callback)
    {
        if (callback == null) return (-1, null);

        int callbackId = _Counter++;
        _Table.Add(callbackId, callback);
        return (callbackId, InvokeCallbackIfFound);
    }

    public static bool TryGetValue(int id, out TaskRunner.PacketsCallback callback)
    {
        return _Table.TryGetValue(id, out callback);
    }

    private static void InvokeCallbackIfFound(int callbackId, IntPtr statusPtr, IntPtr packetMapPtr)
    {
        // NOTE: if status is not OK, packetMap will be nullptr
        if (packetMapPtr == IntPtr.Zero)
        {
            Status status = new(statusPtr, false);
            Console.WriteLine(status.ToString());
            return;
        }

        if (TryGetValue(callbackId, out TaskRunner.PacketsCallback callback))
            try
            {
                callback(new PacketMap(packetMapPtr, false));
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
    }
}