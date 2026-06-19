using System.Runtime.CompilerServices;
using T3.VideoServices;

namespace Video;

/// <summary>
/// Publishes the FFmpeg video-export factory into Core's <c>VideoExport.Factory</c> so the editor's
/// render-export can reach it across the operator load-context boundary (the editor cannot depend on the
/// FFmpeg <c>VideoServices</c> assembly directly). Runs when this package's code first executes; idempotent.
/// The render-export side also triggers it on demand so an export with no video operator still finds the encoder.
/// </summary>
internal static class VideoExportRegistration
{
    // CA2255: a module initializer in a library is intentional here — it registers the cross-context FFmpeg
    // encoder factory when this operator package loads, which is exactly the scenario the attribute enables.
#pragma warning disable CA2255
    [ModuleInitializer]
    internal static void Initialize() => FfmpegVideoEncoderFactory.Register();
#pragma warning restore CA2255
}
