#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Output;
using T3.Core.Output.Streaming;
using T3.Editor.App;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Drives what the active setup presents each frame: every display-bound output's composite goes to that
/// display's window (see <see cref="OutputWindowHandling"/>), and every stream-bound output's composite goes to its
/// stream sender, whose lifetime is owned here. The pixels themselves come from <see cref="OutputCompositor"/>.
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
        OutputWindowHandling.BeginFrame();

        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
        {
            DisposeStreamSenders();
            OutputWindowHandling.EndFrame();
            return;
        }

        _activeStreamPlugs.Clear();
        foreach (var output in setup.Outputs)
        {
            // Send=false pauses presenting this output without dropping its binding.
            if (!output.IsSending)
                continue;

            var binding = machineConfig.FindBinding(output.Id);
            if (binding == null)
                continue;

            if (binding.IsStream)
            {
                SendToStream(machineConfig, output, binding);
                continue;
            }

            // RenderOutput returns null when there's nothing to composite (no active send op for this
            // output, empty target list, paused update); the window then keeps its last frame.
            OutputWindowHandling.Present(binding.DisplayIndex, OutputCompositor.RenderOutput(output.Id));
        }

        SweepStreamSenders();
        OutputWindowHandling.EndFrame();
    }

    /// <summary>Why the stream plug's sender refused its last frame, if it did (e.g. an unsupported format).</summary>
    public static bool TryGetStreamError(Guid plugId, out string error)
    {
        error = string.Empty;
        if (!_streamSenders.TryGetValue(plugId, out var slot) || string.IsNullOrEmpty(slot.LastError))
            return false;

        error = slot.LastError;
        return true;
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
        DisposeStreamSenders();
        OutputWindowHandling.HideAll();
    }

    /// <summary>Frees a deleted output's composite target; the memos keyed on it drop out with the next frame.</summary>
    public static void ReleaseOutput(Guid outputId)
    {
        OutputCompositor.ReleaseOutput(outputId);
    }

    /// <summary>
    /// Pushes an output's composite into its stream sender, opening (or re-opening after a rename) the sender
    /// from the plug's provider. A plug whose package isn't loaded sends nothing and keeps its binding.
    /// </summary>
    private static void SendToStream(MachineConfig machineConfig, OutputDefinition output, PlugBinding binding)
    {
        var stream = machineConfig.FindStreamPlug(binding.PlugId);
        if (stream == null)
            return;

        _activeStreamPlugs.Add(stream.Id);

        // The provider is looked up every frame: a sender must not outlive the package that implements it,
        // and a package reload replaces the provider instance.
        var provider = OutputStreamRegistry.TryGetProvider(stream.Kind);
        if (_streamSenders.TryGetValue(stream.Id, out var slot)
            && (slot.Provider != provider || slot.Name != stream.Name))
        {
            slot.Sender.Dispose();
            _streamSenders.Remove(stream.Id);
            slot = null;
        }

        if (provider == null)
            return;

        if (slot == null)
        {
            slot = new OpenStream(stream.Name, provider, provider.CreateSender(stream.Name));
            _streamSenders[stream.Id] = slot;
        }

        slot.Sender.Configure(stream.ToSettings());
        var composite = OutputCompositor.RenderOutput(output.Id);
        if (composite == null)
            return;

        slot.Sender.Send(composite);
        slot.LastError = slot.Sender.LastError;
    }

    /// <summary>Closes senders whose plug wasn't sent to this frame (binding dropped, output paused or deleted).</summary>
    private static void SweepStreamSenders()
    {
        if (_streamSenders.Count == 0)
            return;

        _staleStreamPlugs.Clear();
        foreach (var entry in _streamSenders)
        {
            if (!_activeStreamPlugs.Contains(entry.Key))
                _staleStreamPlugs.Add(entry.Key);
        }

        foreach (var plugId in _staleStreamPlugs)
        {
            _streamSenders[plugId].Sender.Dispose();
            _streamSenders.Remove(plugId);
        }
    }

    private static void DisposeStreamSenders()
    {
        foreach (var slot in _streamSenders.Values)
            slot.Sender.Dispose();

        _streamSenders.Clear();
    }

    private sealed record OpenStream(string Name, IOutputStreamProvider Provider, IOutputStreamSender Sender)
    {
        public string? LastError;
    }

    private static readonly Dictionary<Guid, OpenStream> _streamSenders = [];
    private static readonly HashSet<Guid> _activeStreamPlugs = [];
    private static readonly List<Guid> _staleStreamPlugs = [];
}
