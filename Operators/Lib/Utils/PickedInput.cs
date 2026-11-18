using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Utils;

namespace Lib.Utils;

/// <summary>
/// Shared evaluation for ops that pass on one of several connected inputs, like [PickMeshBuffer] or [PickFloat].
/// </summary>
internal static class PickedInput
{
    /// <summary>
    /// Evaluates only the connected input at <paramref name="index"/> (wrapped around the connection count) and
    /// limits the invalidation of <paramref name="input"/> to it. Returns false if nothing is connected.
    /// </summary>
    public static bool TryGetValue<T>(MultiInputSlot<T> input, int index, EvaluationContext context, out T value)
    {
        var connections = input.GetCollectedTypedInputs();
        if (connections.Count == 0)
        {
            input.LimitMultiInputInvalidationToIndices = null;
            input.DirtyFlag.Clear();
            value = default;
            return false;
        }

        var pickedIndex = index.Mod(connections.Count);

        // Unpicked inputs are never evaluated. Walking them would keep them dirty and invalidate this op
        // and everything downstream on every frame.
        var limit = input.LimitMultiInputInvalidationToIndices;
        if (limit is not { Length: 1 })
        {
            limit = new int[1];
            input.LimitMultiInputInvalidationToIndices = limit;
        }

        limit[0] = pickedIndex;

        // A newly picked input was skipped by this frame's walk and hasn't seen upstream changes yet.
        var picked = connections[pickedIndex];
        picked.InvalidateGraph();
        value = picked.GetValue(context);

        input.DirtyFlag.Clear();
        return true;
    }
}
