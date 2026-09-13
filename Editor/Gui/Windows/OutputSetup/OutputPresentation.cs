#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Output;
using T3.Core.Output.Streaming;
using T3.Editor.App;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Layouts;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Drives what the active setup presents each frame: the composite of the output bound to the secondary
/// display goes to the viewer window, and every stream-bound output's composite goes to its stream sender,
/// whose lifetime is owned here. The pixels themselves come from <see cref="OutputCompositor"/>.
/// </summary>
internal static class OutputPresentation
{
    /// <summary>The output currently presented on the secondary display window (Guid.Empty = none).</summary>
    public static Guid PresentedOutputId;

    /// <summary>
    /// Per-frame driver: renders each display-bound output's composite (so its content evaluates even
    /// when nothing displays it) and presents it on its bound display. A binding is the intent to
    /// present, so this auto-resumes a persisted binding after a restart. Skipped while the second
    /// view mirrors the editor UI. Call before the viewer's back buffer is bound for the frame.
    /// </summary>
    public static void UpdatePresentation()
    {
        if (!OutputSetupHandling.TryGetActiveSetup(out var setup, out var machineConfig))
        {
            DisposeStreamSenders();
            return;
        }

        // Streams send regardless of the second window; the display path below yields to the UI mirror.
        var mirrorUi = UserSettings.Config.MirrorUiOnSecondView;
        OutputDefinition? boundOutput = null;
        PlugBinding? binding = null;
        _activeStreamPlugs.Clear();
        foreach (var output in setup.Outputs)
        {
            // Send=false pauses presenting this output without dropping its binding.
            if (!output.IsSending)
                continue;

            var candidate = machineConfig.FindBinding(output.Id);
            if (candidate == null)
                continue;

            if (candidate.IsStream)
            {
                SendToStream(machineConfig, output, candidate);
                continue;
            }

            // One display can be driven today: the first bound output takes it.
            if (boundOutput == null && !mirrorUi)
            {
                boundOutput = output;
                binding = candidate;
            }
        }

        SweepStreamSenders();

        if (boundOutput == null || binding == null)
        {
            // Nothing presentable (unbound, or Send paused) — take down the second window if it was up.
            HidePresentation();
            return;
        }

        PresentedOutputId = boundOutput.Id;

        // RenderOutput returns null when there's nothing to composite (no active send op for this
        // output, empty target list, paused update). Assigning null trips the Texture2D→SharpDX
        // implicit conversion (dereferences TextureObject) — keep the last presented frame instead.
        var composite = OutputCompositor.RenderOutput(boundOutput.Id);
        if (composite != null)
            ProgramWindows.Viewer.Texture = composite;

        if (!WindowManager.ShowSecondaryRenderWindow || _presentedDisplayIndex != binding.DisplayIndex)
        {
            WindowManager.ShowSecondaryRenderWindow = true;
            ProgramWindows.Viewer.SetFullScreen(binding.DisplayIndex);
            _presentedDisplayIndex = binding.DisplayIndex;
        }
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
        HidePresentation();
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

    private static void HidePresentation()
    {
        if (PresentedOutputId == Guid.Empty)
            return;

        WindowManager.ShowSecondaryRenderWindow = false;
        PresentedOutputId = Guid.Empty;
        _presentedDisplayIndex = -1;
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
    private static int _presentedDisplayIndex = -1;
}
