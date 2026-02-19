using Google.Protobuf;
using Mediapipe.Core;
using Mediapipe.External;
using Mediapipe.Framework.Packet;
using Mediapipe.Gpu;
using Mediapipe.PInvoke;

namespace Mediapipe.Tasks.Core;

public class TaskRunner : MpResourceHandle
{
    public delegate void NativePacketsCallback(int name, IntPtr status, IntPtr packetMap);

    public delegate void PacketsCallback(PacketMap packetMap);

    private TaskRunner(IntPtr ptr) : base(ptr)
    {
    }

    public static TaskRunner Create(CalculatorGraphConfig config, GpuResources gpuResources, int callbackId = -1,
        NativePacketsCallback? packetsCallback = null)
    {
        byte[]? bytes = config.ToByteArray();
        IntPtr gpuResourcesPtr = gpuResources == null ? IntPtr.Zero : gpuResources.SharedPtr;
        UnsafeNativeMethods.mp_tasks_core_TaskRunner_Create__PKc_i_PF_Pgr(bytes, bytes.Length, callbackId,
            packetsCallback!, gpuResourcesPtr, out IntPtr statusPtr, out IntPtr taskRunnerPtr).Assert();

        AssertStatusOk(statusPtr);
        return new TaskRunner(taskRunnerPtr);
    }

    public static TaskRunner Create(CalculatorGraphConfig config, int callbackId = -1,
        NativePacketsCallback? packetsCallback = null)
    {
        byte[]? bytes = config.ToByteArray();
        UnsafeNativeMethods.mp_tasks_core_TaskRunner_Create__PKc_i_PF(bytes, bytes.Length, callbackId, packetsCallback!,
            out IntPtr statusPtr, out IntPtr taskRunnerPtr).Assert();

        AssertStatusOk(statusPtr);
        return new TaskRunner(taskRunnerPtr);
    }

    protected override void DeleteMpPtr()
    {
        UnsafeNativeMethods.mp_tasks_core_TaskRunner__delete(Ptr);
    }

    public PacketMap Process(PacketMap inputs)
    {
        UnsafeNativeMethods
            .mp_tasks_core_TaskRunner__Process__Ppm(MpPtr, inputs.MpPtr, out IntPtr statusPtr, out IntPtr packetMapPtr)
            .Assert();
        inputs.Dispose(); // respect move semantics

        AssertStatusOk(statusPtr);
        return new PacketMap(packetMapPtr, true);
    }

    public void Send(PacketMap inputs)
    {
        UnsafeNativeMethods.mp_tasks_core_TaskRunner__Send__Ppm(MpPtr, inputs.MpPtr, out IntPtr statusPtr).Assert();
        inputs.Dispose(); // respect move semantics

        AssertStatusOk(statusPtr);
    }

    public void Close()
    {
        UnsafeNativeMethods.mp_tasks_core_TaskRunner__Close(MpPtr, out IntPtr statusPtr).Assert();

        AssertStatusOk(statusPtr);
    }

    public void Restart()
    {
        UnsafeNativeMethods.mp_tasks_core_TaskRunner__Restart(MpPtr, out IntPtr statusPtr).Assert();

        AssertStatusOk(statusPtr);
    }

    public CalculatorGraphConfig GetGraphConfig(ExtensionRegistry? extensionRegistry = null)
    {
        UnsafeNativeMethods.mp_tasks_core_TaskRunner__GetGraphConfig(MpPtr, out SerializedProto serializedProto)
            .Assert();

        MessageParser<CalculatorGraphConfig>? parser = extensionRegistry == null
            ? CalculatorGraphConfig.Parser
            : CalculatorGraphConfig.Parser.WithExtensionRegistry(extensionRegistry);
        CalculatorGraphConfig config = serializedProto.Deserialize(parser);
        serializedProto.Dispose();

        return config;
    }
}