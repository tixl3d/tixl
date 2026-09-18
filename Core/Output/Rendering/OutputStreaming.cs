#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Logging;
using T3.Core.Output.Streaming;

namespace T3.Core.Output.Rendering;

/// <summary>
/// Pushes stream-bound outputs to their senders (NDI, Spout, whatever a loaded package provides) and owns the
/// senders' lifetime. A host brackets its outputs with <see cref="BeginFrame"/> and <see cref="EndFrame"/>;
/// a sender whose binding went away, or whose output stopped sending, is closed at the end of that frame.
/// </summary>
public static class OutputStreaming
{
    public static void BeginFrame()
    {
        _activeStreamPlugs.Clear();
    }

    /// <summary>
    /// Sends an output's composite through the stream plug it is bound to, opening the sender (or re-opening it
    /// after a rename) from the plug's provider. A plug whose package isn't loaded sends nothing and keeps its
    /// binding, so loading the package later picks it back up.
    /// </summary>
    public static void Send(MachineConfig machineConfig, OutputDefinition output, PlugBinding binding)
    {
        var stream = machineConfig.FindStreamPlug(binding.PlugId);
        if (stream == null)
            return;

        _activeStreamPlugs.Add(stream.Id);

        // Looked up every frame: a sender must not outlive the package that implements it, and a package
        // reload replaces the provider instance.
        var provider = OutputStreamRegistry.TryGetProvider(stream.Kind);
        if (_senders.TryGetValue(stream.Id, out var slot)
            && (slot.Provider != provider || slot.Name != stream.Name))
        {
            slot.Sender.Dispose();
            _senders.Remove(stream.Id);
            slot = null;
        }

        if (provider == null)
        {
            ReportMissingProvider(stream);
            return;
        }

        // A sender that threw is set aside until its provider changes (a package reload), rather than retried
        // every frame — the failure is almost always a missing runtime, which a retry won't fix.
        if (_failedProviders.TryGetValue(stream.Id, out var failedProvider) && failedProvider == provider)
            return;

        try
        {
            if (slot == null)
            {
                slot = new OpenStream(stream.Name, provider, provider.CreateSender(stream.Name));
                _senders[stream.Id] = slot;
            }

            slot.Sender.Configure(stream.ToSettings());
            var composite = OutputCompositor.RenderOutput(output.Id);
            if (composite == null)
                return;

            slot.Sender.Send(composite);
            slot.LastError = slot.Sender.LastError;
        }
        catch (Exception e)
        {
            // A stream going down must not take the rest of the show with it.
            _failedProviders[stream.Id] = provider;
            if (_senders.Remove(stream.Id, out var broken))
                TryDispose(broken.Sender);

            Log.Error($"Stream \"{stream.Name}\" ({stream.Kind}) stopped: {e.Message}");
        }
    }

    /// <summary>Closes the senders no output sent to this frame (binding dropped, output paused or deleted).</summary>
    public static void EndFrame()
    {
        if (_senders.Count == 0)
            return;

        _staleStreamPlugs.Clear();
        foreach (var entry in _senders)
        {
            if (!_activeStreamPlugs.Contains(entry.Key))
                _staleStreamPlugs.Add(entry.Key);
        }

        foreach (var plugId in _staleStreamPlugs)
        {
            _senders[plugId].Sender.Dispose();
            _senders.Remove(plugId);
        }
    }

    /// <summary>Why the stream plug's sender refused its last frame, if it did (e.g. an unsupported format).</summary>
    public static bool TryGetError(Guid plugId, out string error)
    {
        error = string.Empty;
        if (!_senders.TryGetValue(plugId, out var slot) || string.IsNullOrEmpty(slot.LastError))
            return false;

        error = slot.LastError;
        return true;
    }

    /// <summary>Closes every sender — the setup that drove them is going away, or the host is shutting down.</summary>
    public static void DisposeAll()
    {
        foreach (var slot in _senders.Values)
            slot.Sender.Dispose();

        _senders.Clear();
    }

    private static void TryDispose(IOutputStreamSender sender)
    {
        try
        {
            sender.Dispose();
        }
        catch (Exception e)
        {
            Log.Debug($"Disposing a failed stream sender threw: {e.Message}");
        }
    }

    /// <summary>
    /// Said once per plug: an unattended player must not come up silent, but a missing package would otherwise
    /// repeat the line every frame.
    /// </summary>
    private static void ReportMissingProvider(StreamPlug stream)
    {
        if (!_reportedMissingProviders.Add(stream.Id))
            return;

        Log.Warning($"Stream \"{stream.Name}\" needs a {stream.Kind} sender, but no loaded package provides one. "
                    + "It will not be sent.");
    }

    private sealed record OpenStream(string Name, IOutputStreamProvider Provider, IOutputStreamSender Sender)
    {
        public string? LastError;
    }

    private static readonly Dictionary<Guid, OpenStream> _senders = [];
    private static readonly HashSet<Guid> _activeStreamPlugs = [];
    private static readonly List<Guid> _staleStreamPlugs = [];
    private static readonly HashSet<Guid> _reportedMissingProviders = [];
    private static readonly Dictionary<Guid, IOutputStreamProvider> _failedProviders = [];
}
