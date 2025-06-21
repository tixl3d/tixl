using T3.Core.Utils;

namespace Lib.numbers.floats.process;

[Guid("ca6f09ec-bbc4-4365-8210-bc10cd8d9f94")]
internal sealed class MergeIntLists : Instance<MergeIntLists>, IStatusProvider
{
    [Output(Guid = "F28370F0-F0C6-418F-8FBF-167A7D1035FE")]
    public readonly Slot<List<int>> Result = new();

    public MergeIntLists()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _lastErrorMessage = string.Empty;
        
        Result.Value ??= [];
        var list = Result.Value;
        
        var inputListSlots = InputLists.GetCollectedTypedInputs();
        var noValidInputs = inputListSlots == null || inputListSlots.Count == 0;
        list.Clear();

        var listNeedsCleanup = StartIndices.DirtyFlag.IsDirty;
        
        // Initialize list with maxSize if requested
        var maxSize = MaxSize.GetValue(context);
        var useMaxSize = maxSize >= 0;
        if (useMaxSize && maxSize != list.Count || listNeedsCleanup)
        {
            list.Clear();
            list.Capacity = maxSize.Clamp(8, 1024 * 1024);
            for (int i = 0; i < maxSize; i++)
            {
                list.Add(0);
            }
        }

        
        var startIndices = StartIndices.GetValue(context) ?? [];
        
        if (noValidInputs)
            return;
        
        

        try
        {
            var writeIndex = 0;
            for (var listIndex = 0; listIndex < inputListSlots.Count; listIndex++)
            {
                var inputListSlot = inputListSlots[listIndex];
                var source = inputListSlot.GetValue(context);
                if (source == null || source.Count == 0)
                    continue;

                if (listIndex < startIndices.Count)
                {
                    var newStartIndex = startIndices[listIndex];
                    if (newStartIndex < 0)
                    {
                        _lastErrorMessage = $"Skipped negative start index {newStartIndex}";
                    }
                    else if (newStartIndex >= maxSize)
                    {
                        _lastErrorMessage = $"Skipped start index {newStartIndex} exceeding maxSize {maxSize}";
                    }
                    else
                    {
                        writeIndex = newStartIndex;
                    }
                }

                if (useMaxSize)
                {
                    for (var indexInSource = 0; indexInSource < source.Count && indexInSource < maxSize && writeIndex < maxSize ; indexInSource++)
                    {
                        if (writeIndex >= 0)
                            list[writeIndex] = source[indexInSource];

                        writeIndex++;
                    }

                    if (writeIndex >= maxSize)
                    {
                        _lastErrorMessage = $"Index exceeds max size of {maxSize}";
                    }
                }
                else
                {
                    for (var indexInSource = 0; indexInSource < source.Count; indexInSource++)
                    {
                        var value = source[indexInSource];
                        if (writeIndex < list.Count)
                        {
                            list[writeIndex++] = value;
                            continue;
                        }

                        while (writeIndex > list.Count )
                        {
                            list.Add(-1);
                        }

                        list.Add(value);

                        writeIndex++;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning("Failed to merge lists " + e.Message, this);
        }
    }

    [Input(Guid = "BDFE5576-2F45-473D-BB9D-95FC453FC774")]
    public readonly MultiInputSlot<System.Collections.Generic.List<int>> InputLists = new MultiInputSlot<System.Collections.Generic.List<int>>();

    [Input(Guid = "9E60F3E7-A891-4A68-B186-21BC0145BDC6")]
    public readonly InputSlot<int> MaxSize = new InputSlot<int>();

    [Input(Guid = "387FB1DB-944F-4EB1-BB6F-B149E4A51A42")]
    public readonly InputSlot<System.Collections.Generic.List<int>> StartIndices = new InputSlot<System.Collections.Generic.List<int>>();

    private string _lastErrorMessage = string.Empty;

    public IStatusProvider.StatusLevel GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    public string GetStatusMessage() => _lastErrorMessage;
}