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
        foreach (var output in setup.Outputs)
        {
            // Send=false pauses presenting this output without dropping its binding.
            if (!output.IsSending)
                continue;

            var binding = machineConfig.FindBinding(output.Id);
            if (binding == null)
                continue;

            var startTimestamp = Stopwatch.GetTimestamp();
            if (binding.IsStream)
            {
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
