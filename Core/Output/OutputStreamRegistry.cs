#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;

namespace T3.Core.Output;

/// <summary>
/// Per-frame record of the resolutions each send's content was pulled at. The host notes every pull; the op
/// reads back whether it was asked for more than one size in the same frame.
///
/// Why it matters: content is invalidated once per frame, so the *first* pull evaluates the graph and every
/// later one gets that texture back. A send routed to two outputs of different sizes therefore renders once —
/// at whichever output composited first — and the other silently shows that size. Cheap, but not what the
/// setup says, so the op says it out loud rather than leaving it to be found on a wall.
/// </summary>
public static class OutputContentStats
{
    /// <summary>Notes that <paramref name="symbolChildId"/>'s content was pulled at this resolution.</summary>
    public static void NotePull(Guid symbolChildId, Int2 resolution, int frame)
    {
        lock (_pulls)
        {
            if (frame != _frame)
            {
                _frame = frame;
                _pulls.Clear();
            }

            if (!_pulls.TryGetValue(symbolChildId, out var entry))
            {
                _pulls[symbolChildId] = new Pull(resolution, resolution);
                return;
            }

            if (entry.First.Width == resolution.Width && entry.First.Height == resolution.Height)
                return;

            _pulls[symbolChildId] = entry with { Other = resolution };
        }
    }

    /// <summary>True when this send was asked for two different sizes in the last recorded frame.</summary>
    public static bool TryGetSizeConflict(Guid symbolChildId, out Int2 rendered, out Int2 other)
    {
        lock (_pulls)
        {
            if (_pulls.TryGetValue(symbolChildId, out var entry)
                && (entry.First.Width != entry.Other.Width || entry.First.Height != entry.Other.Height))
            {
                rendered = entry.First;
                other = entry.Other;
                return true;
            }
        }

        rendered = default;
        other = default;
        return false;
    }

    private readonly record struct Pull(Int2 First, Int2 Other);

    private static readonly Dictionary<Guid, Pull> _pulls = new();
    private static int _frame = -1;
}

/// <summary>Settings a stream can be given, where its transport has a notion of them (see
/// <see cref="IOutputStreamProvider.Supported"/>). Frame size is never among them: it follows the texture.</summary>
public readonly record struct OutputStreamSettings(int FrameRate, bool EnableAlpha);

/// <summary>Which of <see cref="OutputStreamSettings"/> a stream kind actually honours, so a host can offer
/// exactly those and no dead controls.</summary>
[Flags]
public enum OutputStreamOptions
{
    None = 0,
    FrameRate = 1,
    Alpha = 2,
}

/// <summary>
/// A kind of network/IPC video stream this machine can send (Spout, NDI, …). Implemented by operator packages
/// that carry the native library; discovered by type when the package assembly loads, so the host can offer a
/// stream plug without referencing the package. One instance per loaded package assembly.
/// </summary>
public interface IOutputStreamProvider
{
    /// <summary>Stable identifier persisted in machine configs (e.g. "Spout", "NDI").</summary>
    string Kind { get; }

    /// <summary>The settings senders of this kind honour; the rest are not offered for it.</summary>
    OutputStreamOptions Supported { get; }

    /// <summary>Opens a sender under the given name. Frame size follows the textures sent.</summary>
    IOutputStreamSender CreateSender(string name);
}

/// <summary>A live sender: the host pushes each presented frame; the sender owns its native resources.</summary>
public interface IOutputStreamSender : IDisposable
{
    string Name { get; }

    /// <summary>Applies the plug's settings; called before each <see cref="Send"/>, so an edit takes effect at
    /// once. A sender ignores whatever its transport has no notion of.</summary>
    void Configure(OutputStreamSettings settings);

    /// <summary>Sends one frame. False when nothing was sent (no receiver, unsupported format, native failure);
    /// <see cref="LastError"/> then says why, if the sender knows.</summary>
    bool Send(Texture2D texture);

    string? LastError { get; }
}

/// <summary>
/// The stream kinds available on this machine — one <see cref="IOutputStreamProvider"/> per loaded package
/// that implements one. Registration follows the package assembly's lifetime (see AssemblyInformation).
/// </summary>
public static class OutputStreamRegistry
{
    /// <summary>Bumped whenever membership changes, so hosts can rebuild their plug inventory lazily.</summary>
    public static int Version { get; private set; }

    public static IReadOnlyList<IOutputStreamProvider> Providers => _providers;

    public static void Register(IOutputStreamProvider provider)
    {
        lock (_providers)
        {
            for (var i = 0; i < _providers.Count; i++)
            {
                if (_providers[i].Kind == provider.Kind)
                    return;
            }

            _providers.Add(provider);
            Version++;
        }
    }

    public static void Unregister(IOutputStreamProvider provider)
    {
        lock (_providers)
        {
            if (_providers.Remove(provider))
                Version++;
        }
    }

    public static IOutputStreamProvider? TryGetProvider(string kind)
    {
        lock (_providers)
        {
            for (var i = 0; i < _providers.Count; i++)
            {
                if (_providers[i].Kind == kind)
                    return _providers[i];
            }
        }

        return null;
    }

    private static readonly List<IOutputStreamProvider> _providers = [];
}
