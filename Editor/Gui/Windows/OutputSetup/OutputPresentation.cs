#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using T3.Core.Output;
using T3.Core.Output.Rendering;
using T3.Editor.App;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Drives what the active setup presents each frame: every display-bound output's composite goes to that
/// display's window (see <see cref="OutputWindowHandling"/>), and every stream-bound output's composite goes to its
/// stream sender (see <see cref="OutputStreaming"/>). The pixels themselves come from <see cref="OutputCompositor"/>.
/// </summary>
internal static class OutputPresentation
{
    /// <summary>
    /// Per-frame driver: renders each bound output's composite (so its content evaluates even when nothing
    /// displays it) and hands it to its display's window or its stream sender. A binding is the intent to
    /// present, so this auto-resumes a persisted binding after a restart. Call before the viewer's back buffer
    /// is bound for the frame.
    /// </summary>
    public static void UpdatePresentation()
    {
        // The failover arbiter sees every rendered frame here. A node that comes back from a freeze
        // longer than the stall window demotes itself before the first output is evaluated, so a zombie
        // master can never send a frame while a standby already took over.
        T3.Core.Show.FailoverArbiter.NotifyFrameAdvanced();

        // One token per frame for everything the compositing path memoises, advanced before anything asks.
        OutputFrame.Advance();
        OutputWindowHandling.BeginFrame();
        OutputPresentationStats.BeginFrame();

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
        {
            OutputStreaming.DisposeAll();
            OutputWindowHandling.EndFrame();
            return;
        }

        OutputStreaming.BeginFrame();

        // Show failover: a Standby machine keeps rendering its local display windows but suppresses
        // every stream-bound (network) output. Disposing the senders matters — NDI/Spout senders
        // would otherwise keep re-sending their last frame from their own loops.
        var failoverSuppressed = T3.Core.Show.FailoverArbiter.IsOutputSuppressed;
        if (failoverSuppressed)
            OutputStreaming.DisposeAll();

        foreach (var output in setup.Outputs)
        {
            // Send=false pauses presenting this output without dropping its binding.
            if (!output.IsSending)
                continue;

            // Show distribution: an output assigned to a specific boygroup node is presented only
            // there. Empty assignment keeps the classic every-machine behavior.
            if (!string.IsNullOrEmpty(output.MachineName) && output.MachineName != Show.BoygroupManager.LocalNodeName)
                continue;

            // A laser output presents nothing here, whatever its bindings say: it has no canvas
            // composite, its frame is vector content streamed by the laser driver. Checked on the
            // output and not just on the binding, so a stale display binding on a laser output can
            // never throw the input full-screen onto a wall, in any binding order.
            if (output.Kind == OutputDefinition.Kinds.Laser || HasLaserBinding(machineConfig, output.Id))
                continue;

            var binding = machineConfig.FindBinding(output.Id);
            if (binding == null)
                continue;

            var startTimestamp = Stopwatch.GetTimestamp();
            if (binding.IsStream)
            {
                if (failoverSuppressed)
                    continue;

                OutputStreaming.Send(machineConfig, output, binding);
            }
            else
            {
                // RenderOutput returns null when there's nothing to composite (no active send op for this
                // output, empty target list, paused update); the window then keeps its last frame.
                OutputWindowHandling.Present(binding.DisplayIndex, OutputCompositor.RenderOutput(output.Id));
            }

            OutputPresentationStats.NotePresented(output, startTimestamp);
        }

        OutputStreaming.EndFrame();
        OutputWindowHandling.EndFrame();
    }

    /// <summary>Whether any binding of this output goes to a laser plug. A laser target is never a
    /// canvas to present - checked per output so binding order can't change the answer.</summary>
    private static bool HasLaserBinding(MachineConfig machineConfig, Guid outputId)
    {
        foreach (var binding in machineConfig.Bindings)
        {
            if (binding.OutputId == outputId && binding.IsLaser)
                return true;
        }

        return false;
    }

    /// <summary>Why the stream plug's sender refused its last frame, if it did (e.g. an unsupported format).</summary>
    public static bool TryGetStreamError(Guid plugId, out string error)
    {
        return OutputStreaming.TryGetError(plugId, out error);
    }

    /// <summary>
    /// Drops everything held for the active setup — composite targets, per-frame memos, stream senders — for
    /// when the setup itself goes away or is swapped (project close, setup switch); the next frame rebuilds
    /// what the new one needs.
    /// </summary>
    public static void ReleaseAll()
    {
        OutputCompositor.ReleaseAll();
        OutputContentResolver.ReleaseAll();
        OutputPresentationStats.ReleaseAll();
        OutputStreaming.DisposeAll();
        OutputWindowHandling.HideAll();
    }

    /// <summary>Frees a deleted output's composite target; the memos keyed on it drop out with the next frame.</summary>
    public static void ReleaseOutput(Guid outputId)
    {
        OutputCompositor.ReleaseOutput(outputId);
        OutputPresentationStats.Release(outputId);
    }
}
