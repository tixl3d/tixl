#nullable enable
using System.Collections.Generic;

namespace T3.Core.Output;

/// <summary>
/// The set of live <see cref="IContentSupplier"/> instances. Suppliers add themselves on construction and
/// remove themselves on dispose, so the registry survives operator hot-reloads (which recreate the
/// instances). Insertion order is preserved; the output manager resolves per output/surface.
/// </summary>
public static class ContentSupplierRegistry
{
    /// <summary>
    /// Bumped whenever membership changes. Lets hosts skip work that can only be invalidated by a send
    /// appearing or disappearing — notably scanning the symbol library to see whether an op was deleted.
    /// </summary>
    public static int Version { get; private set; }

    public static void Register(IContentSupplier supplier)
    {
        if (_suppliers.Contains(supplier))
            return;

        _suppliers.Add(supplier);
        Version++;
    }

    public static void Unregister(IContentSupplier supplier)
    {
        if (_suppliers.Remove(supplier))
            Version++;
    }

    public static IReadOnlyList<IContentSupplier> Suppliers => _suppliers;

    private static readonly List<IContentSupplier> _suppliers = [];
}
