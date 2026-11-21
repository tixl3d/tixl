using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using T3.Core.Model;
using T3.Core.Operator.Slots;

namespace Mediapipe;

/// <summary>
/// macOS: the bundled libmediapipe_c.dylib leaves its OpenCL imports to dynamic lookup, and Apple
/// Silicon macOS ships no OpenCL, so dyld refuses the image unless the symbols already exist in the
/// process ("symbol not found in flat namespace '_clBuildProgram'"). libmediapipe_opencl_stub.dylib
/// (built next to the native by the macOS CI and Installer/macOS/build-dmg.sh) exports harmless
/// no-ops - the CPU delegate paths the operators use never call them. Loading it here runs before
/// the first mediapipe_c P/Invoke in this assembly resolves the real library. No-op elsewhere.
/// </summary>
internal static class MediapipeNativeLibrary
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Best-effort: this assembly is also loaded standalone (unit tests exercise the native tasks
        // without T3.Core on the probe path), and a throwing module initializer would kill the whole
        // process. In the editor Core is always present and the registration runs.
        try
        {
            RegisterAccelerationInputType();
        }
        catch (Exception)
        {
            // Core unavailable - enum inputs fall back to their int values.
        }

        if (!OperatingSystem.IsMacOS())
            return;

        var stub = Path.Combine(AppContext.BaseDirectory, "libmediapipe_opencl_stub.dylib");
        if (File.Exists(stub))
            NativeLibrary.TryLoad(stub, out _);
    }

    /// <summary>
    /// Operator-package types must register themselves with Core's input-value registries for their
    /// enums to be usable as input slot types (the editor reflects the field and looks the type up
    /// here when creating input definitions). Indexer assignments keep this idempotent across
    /// package hot-reloads, which load a second copy of this assembly into the same process.
    /// </summary>
    private static void RegisterAccelerationInputType()
    {
        var type = typeof(Lib.io.video.mediapipe.MediaPipeAcceleration);

        TypeNameRegistry.Entries[type] = "MediaPipeAcceleration";
        InputValueCreators.Entries[type] = () => new InputValue<Lib.io.video.mediapipe.MediaPipeAcceleration>(Lib.io.video.mediapipe.MediaPipeAcceleration.Xnnpack);
        TypeValueToJsonConverters.Entries[type] = (writer, obj) => writer.WriteValue(obj.ToString());
        JsonToTypeValueConverters.Entries[type] = token =>
        {
            if (Enum.TryParse(token.ToString(), out Lib.io.video.mediapipe.MediaPipeAcceleration result))
                return result;

            return Lib.io.video.mediapipe.MediaPipeAcceleration.Xnnpack;
        };
    }
}
