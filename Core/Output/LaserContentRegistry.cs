#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Slots;

namespace T3.Core.Output;

/// <summary>
/// A graph node that supplies a laser frame to the output setup — the laser counterpart of
/// <see cref="IContentSupplier"/>. The node registers itself with <see cref="LaserContentRegistry"/>
/// so the Output Setup can list it as content; routing lives on the node's own OutputRef input
/// (it names the laser output it feeds), not on slices.
/// </summary>
public interface ILaserContentSupplier
{
    /// <summary>The current laser frame (ILDA points), or null when nothing is produced.</summary>
    StructuredList? GetLaserPoints(EvaluationContext context);

    /// <summary>When false the supplier holds its last frame instead of evaluating its content again.</summary>
    bool GetUpdateEnabled(EvaluationContext context);

    /// <summary>The op input behind <see cref="GetUpdateEnabled"/>, so a host edits it through its own undoable
    /// input command rather than writing the slot directly.</summary>
    IInputSlot UpdateInput { get; }

    /// <summary>
    /// The supplier's output-setup routing input (the OutputRef input that names the laser output
    /// this supplier feeds). The setup reads it without evaluating anything and writes it through
    /// the supplier's own undoable input command; a supplier that declares no such input reports
    /// false and routes nowhere.
    /// </summary>
    bool TryGetRoutingInput(out Symbol.Child.Input input);
}

/// <summary>
/// The set of live <see cref="ILaserContentSupplier"/> instances. Suppliers add themselves on
/// construction and remove themselves on dispose, so the registry survives operator hot-reloads.
/// Thread-safe: membership changes from any thread (operator construction can run off the UI
/// thread), readers always get an immutable snapshot - enumeration can never throw mid-change.
/// Insertion order is preserved.
/// </summary>
public static class LaserContentRegistry
{
    /// <summary>Bumped whenever membership changes, so hosts can skip work only invalidated by
    /// a laser camera appearing or disappearing.</summary>
    public static int Version => Volatile.Read(ref _version);

    /// <summary>Upper bound on laser points the content suppliers generate per pull; 0 = uncapped.
    /// The driver derives it from the routed output's bound plugs: a CITP LaserFeedFrame travels as
    /// ONE datagram capped at 12,000 points and the encode drops everything beyond, so converting
    /// more would be work whose result is thrown away. Set before a pull and read during it on the
    /// same evaluation thread; thread-static so an on-demand preview pull can never overwrite the
    /// budget of a pump running concurrently on the render thread.</summary>
    public static int PointBudget
    {
        get => _pointBudget;
        set => _pointBudget = value;
    }

    [ThreadStatic]
    private static int _pointBudget;

    public static void Register(ILaserContentSupplier supplier)
    {
        lock (_lock)
        {
            if (_suppliers.Contains(supplier))
                return;

            _suppliers.Add(supplier);
            Interlocked.Increment(ref _version);
            PublishSnapshot();
        }
    }

    public static void Unregister(ILaserContentSupplier supplier)
    {
        lock (_lock)
        {
            if (!_suppliers.Remove(supplier))
                return;

            Interlocked.Increment(ref _version);
            PublishSnapshot();
        }
    }

    /// <summary>The current members as an immutable snapshot.</summary>
    public static IReadOnlyList<ILaserContentSupplier> Suppliers => Volatile.Read(ref _snapshot);

    private static void PublishSnapshot()
    {
        // Called under _lock only. The volatile publish makes the new array visible to
        // lock-free readers in one piece.
        Volatile.Write(ref _snapshot, _suppliers.ToArray());
    }

    private static int _version;
    private static readonly object _lock = new();
    private static readonly List<ILaserContentSupplier> _suppliers = [];
    private static ILaserContentSupplier[] _snapshot = [];
}
