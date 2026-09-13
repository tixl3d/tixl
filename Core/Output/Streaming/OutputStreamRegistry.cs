#nullable enable
using System.Collections.Generic;

namespace T3.Core.Output.Streaming;

/// <summary>
/// The stream kinds available on this machine — one <see cref="IOutputStreamProvider"/> per loaded package
/// that implements one. Registration follows the package assembly's lifetime (see AssemblyInformation).
/// </summary>
public static class OutputStreamRegistry
{
    /// <summary>Bumped whenever membership changes, so hosts can rebuild their plug inventory lazily.</summary>
    public static int Version { get; private set; }

    /// <summary>A snapshot, so a per-frame walk never races a package (un)load on another thread.</summary>
    public static IReadOnlyList<IOutputStreamProvider> Providers => _providersSnapshot;

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
            _providersSnapshot = _providers.ToArray();
            Version++;
        }
    }

    public static void Unregister(IOutputStreamProvider provider)
    {
        lock (_providers)
        {
            if (!_providers.Remove(provider))
                return;

            _providersSnapshot = _providers.ToArray();
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
    private static IOutputStreamProvider[] _providersSnapshot = [];
}
