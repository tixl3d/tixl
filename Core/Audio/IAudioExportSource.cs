namespace T3.Core.Audio
{
    /// <summary>
    /// Implemented by audio ops (e.g. an [AudioBus]) that must keep being evaluated during video/image
    /// render-export: export only evaluates the exported op-chain, so an op that was driven by a pinned
    /// view or another window would silently stop. Registered instances that report
    /// <see cref="IsActiveForExport"/> when recording starts get their outputs force-evaluated every
    /// exported frame (see <see cref="AudioRendering.EvaluateAllAudioMeteringOutputs"/>).
    /// </summary>
    public interface IAudioExportSource
    {
        /// <summary>True while the op was organically evaluated within the last few frames — the snapshot
        /// taken at export start uses this to only keep sources that were actually live.</summary>
        bool IsActiveForExport { get; }

        /// <summary>
        /// Called once when a render begins, so the source can drop whatever live playback left behind — an
        /// effect's tail, buffered samples — before the first frame is mixed down. Without it a render opens
        /// with a fading remnant of what was last heard in the editor, which also makes it unrepeatable.
        /// </summary>
        void ResetForExport();
    }
}
