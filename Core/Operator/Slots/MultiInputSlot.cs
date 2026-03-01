using System.Collections.Generic;

// ReSharper disable ConvertToAutoPropertyWhenPossible
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable InlineTemporaryVariable
// ReSharper disable LoopCanBeConvertedToQuery

namespace T3.Core.Operator.Slots;

public sealed class MultiInputSlot<T> : InputSlot<T>, IMultiInputSlot
{
    public List<Slot<T>> CollectedInputs => _collectedInputs;
    private readonly List<Slot<T>> _collectedInputs = new(10);
    public int[] LimitMultiInputInvalidationToIndices = [];

    public MultiInputSlot()
    {
        HasInvalidationOverride = true;
    }

    public List<Slot<T>> GetCollectedTypedInputs(bool forceRefresh = false)
    {
        if (!forceRefresh && !DirtyFlag.IsDirty)
            return _collectedInputs;
                
        var inputConnectionCount = InputConnections.Length;
        _collectedInputs.Clear();
            
        for (var i = 0; i < inputConnectionCount; i++)
        {
            var slot = InputConnections[i];
            if (slot.TryGetAsMultiInputTyped(out var multiInput))
            {
                var typedInputs = multiInput.GetCollectedTypedInputs();
                _collectedInputs.AddRange(typedInputs);
            }
            else
            {
                _collectedInputs.Add(slot);
            }
        }

        return _collectedInputs;
    }

    protected override int InvalidationOverride()
    {
        // NOTE: In situations with extremely large graphs (1000 of instances)
        // invalidation can become bottle neck. In these cases it might be justified
        // to limit the invalidation to "active" parts of the subgraph. The [Switch]
        // operator defines this list.

        var collectedInputs = GetCollectedTypedInputs(true);
        var collectedCount = collectedInputs.Count;
            
        int target = 0;
        var multiInputLimitCount = LimitMultiInputInvalidationToIndices.Length;

        if (multiInputLimitCount > 0)
        {
            for (int i = 0; i < multiInputLimitCount; i++)
            {
                var index = LimitMultiInputInvalidationToIndices[i];
                if (index >= collectedCount)
                    continue;

                target += collectedInputs[index].InvalidateGraph();
            }
        }
        else if (collectedCount == 0)
        {
            target += _dirtyFlag.SourceVersion;
        }
        else
        {
            for (int i = 0; i < collectedCount; i++)
            {
                target += collectedInputs[i].InvalidateGraph();
            }
        }

        return target;
    }

    public IReadOnlyList<ISlot> GetCollectedInputs()
    {
        return GetCollectedTypedInputs();
    }

    public void GetValues(ref T[] resources, EvaluationContext context, bool clearDirty= true)
    {
        var connectedInputs = GetCollectedTypedInputs();
        if (connectedInputs.Count != resources.Length)
        {
            resources = new T[connectedInputs.Count];
        }

        for (int i = 0; i < connectedInputs.Count; i++)
        {
            resources[i] = connectedInputs[i].GetValue(context);
        }
            
        if(clearDirty)
            DirtyFlag.Clear();
    }
}