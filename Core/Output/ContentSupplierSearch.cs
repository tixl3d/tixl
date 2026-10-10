#nullable enable
using System;
using System.Collections.Generic;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.Output;

/// <summary>
/// Finds the sends inside an operator — everything an export ships and a player shows. Found by interface
/// rather than by the send operator's id, so a second kind of supplier needs no change here. The search runs on
/// symbols and instantiates only the paths that lead to a send: a large project would otherwise build every op
/// it contains just to be searched.
/// <para>A nested op marked as a composition keeps its sends to itself: the search crosses into it only for
/// sends that opt in through the <see cref="ExposeToParentsInputGuid"/> input.</para>
/// </summary>
public static class ContentSupplierSearch
{
    /// <summary>The id a supplier gives its bool input that makes it visible to the compositions containing its own.</summary>
    public const string ExposeToParentsInputGuid = "1a50c781-1a20-4736-9ec4-60ff2254a537";

    /// <summary>Whether the operator holds a send at any depth, answered from symbols alone.</summary>
    public static bool ContainsAny(Symbol symbol)
    {
        _containsSupplierBySymbol.Clear();
        return ContainsSupplier(symbol);
    }

    /// <summary>
    /// Every send of <paramref name="instance"/>'s composition, at any depth, instantiated on the way. A send registers
    /// itself with <see cref="ContentSupplierRegistry"/> when it is created, so this is also what makes a
    /// host's sends reachable by the compositor.
    /// </summary>
    public static void CollectUnder(Instance instance, List<Instance> suppliers)
    {
        _containsSupplierBySymbol.Clear();
        Collect(instance, suppliers, false);
    }

    /// <summary>
    /// The SymbolChild ids of every send of <paramref name="symbol"/>'s composition, at any depth, from symbols
    /// alone — so a send counts even while nothing has instantiated it.
    /// </summary>
    public static void CollectSupplierChildIds(Symbol symbol, HashSet<Guid> childIds)
    {
        _containsSupplierBySymbol.Clear();
        _visitedSymbols.Clear();
        CollectChildIds(symbol, childIds, false);
    }

    /// <summary>Whether this send stays visible from the compositions that contain the one it sits in.</summary>
    public static bool IsExposedToParents(Symbol.Child send)
    {
        if (!send.Inputs.TryGetValue(_exposeToParentsInputId, out var input))
            return false;

        var value = input.IsDefault ? input.DefaultValue : input.Value;
        return value is InputValue<bool> { Value: true };
    }

    private static void CollectChildIds(Symbol symbol, HashSet<Guid> childIds, bool isInNestedComposition)
    {
        if (!_visitedSymbols.Add((symbol.Id, isInNestedComposition)))
            return;

        foreach (var child in symbol.Children.Values)
        {
            if (IsSupplier(child.Symbol))
            {
                if (!isInNestedComposition || IsExposedToParents(child))
                    childIds.Add(child.Id);
            }
            else if (ContainsSupplier(child.Symbol))
            {
                CollectChildIds(child.Symbol, childIds, isInNestedComposition || child.Symbol.CompositionSettings.Enabled);
            }
        }
    }

    private static void Collect(Instance instance, List<Instance> suppliers, bool isInNestedComposition)
    {
        foreach (var child in instance.Symbol.Children.Values)
        {
            var isSupplier = IsSupplier(child.Symbol);
            if (!isSupplier && !ContainsSupplier(child.Symbol))
                continue;

            if (isSupplier && isInNestedComposition && !IsExposedToParents(child))
                continue;

            if (!instance.Children.TryGetChildInstance(child.Id, out var childInstance))
                continue;

            if (isSupplier)
            {
                suppliers.Add(childInstance);
            }
            else
            {
                Collect(childInstance, suppliers, isInNestedComposition || child.Symbol.CompositionSettings.Enabled);
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
    private static readonly HashSet<(Guid SymbolId, bool IsInNestedComposition)> _visitedSymbols = new();
    private static readonly Guid _exposeToParentsInputId = new(ExposeToParentsInputGuid);
}
