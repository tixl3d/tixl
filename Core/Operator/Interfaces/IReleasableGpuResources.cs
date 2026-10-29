#nullable enable
using System;
using System.Collections.Generic;

namespace T3.Core.Operator.Interfaces;

/// <summary>
/// An operator whose GPU resources can be dropped and rebuilt from its inputs at the cost of one re-render.
/// The editor releases them for operators no evaluation has reached for a while, or when GPU memory runs short.
/// Operators holding content a re-render would not reproduce (accumulation, frozen frames) must decline.
/// </summary>
public interface IReleasableGpuResources
{
    /// <summary>Roughly what releasing would free, so that under memory pressure the largest go first.</summary>
    long ReleasableBytes { get; }

    /// <summary>
    /// Disposes the resources and invalidates the outputs, so the next evaluation rebuilds them. False if nothing
    /// is held or the current content could not be rebuilt.
    /// </summary>
    bool TryReleaseGpuResources();
}

/// <summary>
/// Where implementers of <see cref="IReleasableGpuResources"/> register. Walking the instance tree instead would
/// create every child it enumerates.
/// </summary>
public static class ReleasableGpuResources
{
    public static void Register(IReleasableGpuResources owner)
    {
        lock (_owners)
        {
            _owners.Add(new WeakReference<IReleasableGpuResources>(owner));
        }
    }

    public static void Unregister(IReleasableGpuResources owner)
    {
        lock (_owners)
        {
            for (var index = _owners.Count - 1; index >= 0; index--)
            {
                if (_owners[index].TryGetTarget(out var registered) && registered == owner)
                {
                    _owners.RemoveAt(index);
                    return;
                }
            }
        }
    }

    /// <summary>Adds the owners still alive to <paramref name="result"/> and forgets collected ones.</summary>
    public static void CollectLive(List<IReleasableGpuResources> result)
    {
        lock (_owners)
        {
            for (var index = _owners.Count - 1; index >= 0; index--)
            {
                if (_owners[index].TryGetTarget(out var owner))
                    result.Add(owner);
                else
                    _owners.RemoveAt(index);
            }
        }
    }

    /// <summary>Weak, so an instance that was never disposed can still be collected.</summary>
    private static readonly List<WeakReference<IReleasableGpuResources>> _owners = [];
}
