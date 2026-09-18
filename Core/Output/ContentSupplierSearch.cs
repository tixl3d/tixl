#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Operator;

namespace T3.Core.Output;

/// <summary>
/// Finds the sends inside an operator — everything an export ships and a player shows. Found by interface
/// rather than by the send operator's id, so a second kind of supplier needs no change here. The search runs on
/// symbols and instantiates only the paths that lead to a send: a large project would otherwise build every op
/// it contains just to be searched.
/// </summary>
public static class ContentSupplierSearch
{
    /// <summary>Whether the operator holds a send at any depth, answered from symbols alone.</summary>
    public static bool ContainsAny(Symbol symbol)
    {
        _containsSupplierBySymbol.Clear();
        return ContainsSupplier(symbol);
    }

    /// <summary>
    /// Every send under <paramref name="instance"/>, at any depth, instantiated on the way. A send registers
    /// itself with <see cref="ContentSupplierRegistry"/> when it is created, so this is also what makes a
    /// host's sends reachable by the compositor.
    /// </summary>
    public static void CollectUnder(Instance instance, List<Instance> suppliers)
    {
        _containsSupplierBySymbol.Clear();
        Collect(instance, suppliers);
    }

    private static void Collect(Instance instance, List<Instance> suppliers)
    {
        foreach (var child in instance.Symbol.Children.Values)
        {
            var isSupplier = IsSupplier(child.Symbol);
            if (!isSupplier && !ContainsSupplier(child.Symbol))
                continue;

            if (!instance.Children.TryGetChildInstance(child.Id, out var childInstance))
                continue;

            if (isSupplier)
            {
                suppliers.Add(childInstance);
            }
            else
            {
                Collect(childInstance, suppliers);
            }
        }
    }

    /** Memoised per search: a symbol used in many places is looked through once. */
    private static bool ContainsSupplier(Symbol symbol)
    {
        if (_containsSupplierBySymbol.TryGetValue(symbol.Id, out var known))
            return known;

        _containsSupplierBySymbol[symbol.Id] = false;
        var found = false;
        foreach (var child in symbol.Children.Values)
        {
            if (IsSupplier(child.Symbol) || ContainsSupplier(child.Symbol))
            {
                found = true;
                break;
            }
        }

        _containsSupplierBySymbol[symbol.Id] = found;
        return found;
    }

    private static bool IsSupplier(Symbol symbol)
    {
        return symbol.InstanceType != null && typeof(IContentSupplier).IsAssignableFrom(symbol.InstanceType);
    }

    private static readonly Dictionary<Guid, bool> _containsSupplierBySymbol = new();
}
