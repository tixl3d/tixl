using T3.Core.Utils;

// Note: The original code had a using statement for a specific animation library.
// This is kept for completeness, but may not be necessary if TriggerAnim is not used elsewhere.
// using static Lib.numbers.anim.animators.TriggerAnim;

namespace Lib.numbers.ints
{
    [Guid("ca6f09ec-bbc4-4365-8210-bc10cd8d9f94")]
    internal sealed class MergeIntLists : Instance<MergeIntLists>, IStatusProvider
    {
        private readonly List<int> _ltpCombinedList = new(); // Persistent state for LTP
        private readonly List<List<int>> _previousSourceLists = new(); // Persistent state for FailOver change detection

        [Input(Guid = "24855D2A-595B-4E3E-81C5-65481C262F64")]
        public readonly InputSlot<bool> Enabled = new();

        [Input(Guid = "BDFE5576-2F45-473D-BB9D-95FC453FC774")]
        public readonly MultiInputSlot<List<int>> InputLists = new();

        [Input(Guid = "9E60F3E7-A891-4E3E-81C5-65481C262F64")] // Using a new GUID as MaxSize conflicts (24855D2A-595B-4E3E-81C5-65481C262F64)
        public readonly InputSlot<int> MaxSize = new();

        [Input(Guid = "e3315721-2853-449a-af6a-43cd18400470", MappedType = typeof(MergeModes))]
        public readonly InputSlot<int> MergeMode = new();

        [Output(Guid = "F28370F0-F0C6-418F-8FBF-167A7D1035FE")]
        public readonly Slot<List<int>> Result = new();

        [Input(Guid = "387FB1DB-944F-4EB1-BB6F-B149E4A51A42")]
        public readonly InputSlot<List<int>> StartIndices = new();

        // --- STATE-TRACKING FIELDS ---
        private int _activeFailoverIndex;

        private string _lastErrorMessage = string.Empty;

        public MergeIntLists()
        {
            Result.UpdateAction += Update;
        }

        public IStatusProvider.StatusLevel GetStatusLevel()
        {
            return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
        }

        public string GetStatusMessage()
        {
            return _lastErrorMessage;
        }

        private void Update(EvaluationContext context)
        {
            _lastErrorMessage = string.Empty;

            var inputListSlots = InputLists.GetCollectedTypedInputs();
            if (inputListSlots == null || inputListSlots.Count == 0)
            {
                Result.Value?.Clear();
                _activeFailoverIndex = 0; // Reset FailOver state when disconnected
                _ltpCombinedList.Clear(); // Reset LTP state when disconnected
                _previousSourceLists.Clear(); // Reset FailOver change detection state when disconnected
                return;
            }

            Result.Value ??= new List<int>();
            var resultList = Result.Value;
            resultList.Clear(); // Clear the output list for the current frame

            var mergeModesEnabled = Enabled.GetValue(context);
            if (!mergeModesEnabled)
            {
                UpdateAppend(context, resultList, inputListSlots);
                return;
            }

            var mode = (MergeModes)MergeMode.GetValue(context);

            // Note: We get the lists themselves here, not the slots, for processing
            // Filter out null lists at this stage to simplify subsequent logic
            var sourceLists = inputListSlots.Select(slot => slot.GetValue(context)).ToList();

            // If no valid source lists after filtering, handle gracefully
            if (!sourceLists.Any(l => l != null && l.Any())) // Check if there's *any* non-empty list
            {
                // Only clear _ltpCombinedList if it's truly empty or if there's no active input to justify its state
                // However, the general update logic already clears on full disconnect.
                // For LTP, if all inputs suddenly become empty, it should retain its last state (values persist)
                // but the output might be empty or reflect previous values.
                // Let's refine this - if `sourceLists` contains ONLY nulls or empty lists, we should still handle `_ltpCombinedList`
                // but `resultList` will likely be empty.
                // The current LTP implementation below effectively handles this: `currentMaxInputLength` would be 0, so _ltpCombinedList doesn't grow,
                // and it would output the _ltpCombinedList, which would be empty if it started empty.
                // If `validLists.Any()` is false inside `UpdateLtp`, it would leave `_ltpCombinedList` as is.

                // This block is only if there are NO inputs *at all*, the one above handles that.
                // If inputs exist but are all empty, LTP/HTP/Avg might produce an empty list, FailOver might keep last state.
                // The current approach delegates specific handling to each UpdateX method.
            }

            try
            {
                switch (mode)
                {
                    case MergeModes.Htp:
                        UpdateHtp(sourceLists, resultList);
                        break;
                    case MergeModes.Ltp:
                        UpdateLtp(sourceLists, resultList); // Corrected LTP method
                        break;
                    case MergeModes.FailOver:
                        UpdateFailOver(sourceLists, resultList); // Corrected FailOver method
                        break;
                    case MergeModes.Average:
                        UpdateAverage(sourceLists, resultList);
                        break;
                    case MergeModes.Append:
                    default:
                        // The Append method needs the slots for its logic, not the processed lists
                        UpdateAppend(context, resultList, inputListSlots);
                        break;
                }
            }
            catch (Exception e)
            {
                Log.Warning("Failed to merge lists: " + e.Message, this);
                _lastErrorMessage = e.Message;
            }
        }

        private void UpdateHtp(List<List<int>> sourceLists, List<int> resultList)
        {
            var validLists = sourceLists.Where(l => l != null).ToList();
            if (!validLists.Any()) return;

            var maxLength = validLists.Max(list => list.Count);
            for (var i = 0; i < maxLength; i++)
            {
                var maxValue = int.MinValue;
                var valueFound = false;
                foreach (var sourceList in validLists)
                {
                    if (i < sourceList.Count)
                    {
                        if (!valueFound || sourceList[i] > maxValue)
                        {
                            maxValue = sourceList[i];
                        }

                        valueFound = true;
                    }
                }

                resultList.Add(valueFound ? maxValue : 0);
            }
        }

        // Corrected LTP method with state persistence
        private void UpdateLtp(List<List<int>> sourceLists, List<int> resultList)
        {
            var validLists = sourceLists.Where(l => l != null).ToList();

            // 1. Determine the maximum length required by any currently connected valid input list.
            int currentMaxInputLength = 0;
            if (validLists.Any())
            {
                currentMaxInputLength = validLists.Max(list => list.Count);
            }

            // 2. Ensure _ltpCombinedList is large enough to accommodate at least the current maximum input length.
            //    It only grows here, preserving any values at indices beyond current inputs from previous frames.
            while (_ltpCombinedList.Count < currentMaxInputLength)
            {
                _ltpCombinedList.Add(0); // Pad new elements with a default value
            }

            // 3. Update _ltpCombinedList with values from current inputs.
            //    The order of 'validLists' (derived from InputLists) determines precedence.
            //    Values from later lists will overwrite values from earlier lists at the same index.
            foreach (var sourceList in validLists)
            {
                for (var i = 0; i < sourceList.Count; i++)
                {
                    // Ensure we only write within the current bounds of _ltpCombinedList,
                    // which has already been expanded to at least currentMaxInputLength.
                    // This means values at indices not covered by current inputs will persist.
                    _ltpCombinedList[i] = sourceList[i];
                }
            }

            // 4. The resultList should reflect the full current state of _ltpCombinedList.
            //    This ensures that persistent values (even if current inputs are shorter) are output.
            resultList.AddRange(_ltpCombinedList);
        }

        // Corrected FailOver method with change detection and prioritization
        private void UpdateFailOver(List<List<int>> sourceLists, List<int> resultList)
        {
            // Ensure our state-tracking list has the same number of entries as the source lists
            while (_previousSourceLists.Count < sourceLists.Count)
            {
                _previousSourceLists.Add(null);
            }

            while (_previousSourceLists.Count > sourceLists.Count)
            {
                _previousSourceLists.RemoveAt(_previousSourceLists.Count - 1);
            }

            // Determine if the currently active list has changed
            var activeListHasChanged = false;
            if (_activeFailoverIndex >= 0 && _activeFailoverIndex < sourceLists.Count)
            {
                var currentActiveList = sourceLists[_activeFailoverIndex];
                var previousActiveList = _previousSourceLists[_activeFailoverIndex];

                activeListHasChanged = currentActiveList is { Count: > 0 } &&
                                       !currentActiveList.SequenceEqual(previousActiveList ?? Empty<int>());
            }

            // High-priority check: ALWAYS check if the first list is active again (non-empty and changing).
            // This ensures it returns to primary when it recovers.
            var firstList = sourceLists.FirstOrDefault();
            var previousFirstList = _previousSourceLists.FirstOrDefault();
            if (firstList is { Count: > 0 } && !firstList.SequenceEqual(previousFirstList ?? Empty<int>()))
            {
                _activeFailoverIndex = 0; // Switch back to the primary list
            }
            // If the primary isn't active, and our current active list has stopped changing, find the next active one.
            else if (!activeListHasChanged)
            {
                var foundNextActive = false;
                // Search for the first valid (non-null, non-empty, and changing) list in order of priority.
                for (var i = 0; i < sourceLists.Count; i++)
                {
                    var nextList = sourceLists[i];
                    var prevNextList = (i < _previousSourceLists.Count) ? _previousSourceLists[i] : null;
                    if (nextList is { Count: > 0 } && !nextList.SequenceEqual(prevNextList ?? Empty<int>()))
                    {
                        _activeFailoverIndex = i; // Switch to the new active list
                        foundNextActive = true;
                        break;
                    }
                }

                // If no list has changed, stick to the current index unless it's invalid.
                // If the current index is out of bounds (e.g., input counts changed), reset to 0.
                if (!foundNextActive && _activeFailoverIndex >= sourceLists.Count)
                {
                    _activeFailoverIndex = 0;
                }

                // If no list is active and currently selected list becomes empty, default to 0.
                if (!foundNextActive && (_activeFailoverIndex >= sourceLists.Count || sourceLists[_activeFailoverIndex] is not { Count: > 0 }))
                {
                    _activeFailoverIndex = 0;
                }
            }

            // Use the determined active list for the output
            if (_activeFailoverIndex >= 0 && _activeFailoverIndex < sourceLists.Count)
            {
                var finalList = sourceLists[_activeFailoverIndex];
                if (finalList != null)
                {
                    resultList.AddRange(finalList);
                }
            }

            // Crucial final step: update the previous state for the next frame's comparison.
            // Deep copy lists to prevent mutation issues.
            for (var i = 0; i < sourceLists.Count; i++)
            {
                _previousSourceLists[i] = sourceLists[i] != null ? new List<int>(sourceLists[i]) : null;
            }
        }

        private void UpdateAverage(List<List<int>> sourceLists, List<int> resultList)
        {
            var validLists = sourceLists.Where(l => l != null).ToList();
            if (!validLists.Any()) return;

            var maxLength = validLists.Max(list => list.Count);
            for (var i = 0; i < maxLength; i++)
            {
                long sum = 0;
                var count = 0;
                foreach (var sourceList in validLists)
                {
                    if (i < sourceList.Count)
                    {
                        sum += sourceList[i];
                        count++;
                    }
                }

                resultList.Add(count > 0 ? (int)(sum / count) : 0);
            }
        }

        private void UpdateAppend(EvaluationContext context, List<int> list, List<Slot<List<int>>> inputListSlots)
        {
            var listNeedsCleanup = StartIndices.DirtyFlag.IsDirty;
            var maxSize = MaxSize.GetValue(context);
            var useMaxSize = maxSize >= 0;

            if (useMaxSize && maxSize != list.Count || listNeedsCleanup)
            {
                list.Clear();
                list.Capacity = maxSize.Clamp(8, 1024 * 1024);
                for (var i = 0; i < maxSize; i++)
                {
                    list.Add(0);
                }
            }

            var startIndices = StartIndices.GetValue(context) ?? new List<int>();

            var writeIndex = 0;
            for (var listIndex = 0; listIndex < inputListSlots.Count; listIndex++)
            {
                var source = inputListSlots[listIndex].GetValue(context);
                if (source == null || source.Count == 0)
                    continue;

                if (listIndex < startIndices.Count)
                {
                    var newStartIndex = startIndices[listIndex];
                    if (newStartIndex < 0)
                    {
                        _lastErrorMessage = $"Skipped negative start index {newStartIndex}";
                    }
                    else if (useMaxSize && newStartIndex >= maxSize)
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
                    for (var indexInSource = 0; indexInSource < source.Count && writeIndex < maxSize; indexInSource++)
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
                        }
                        else
                        {
                            while (writeIndex > list.Count)
                            {
                                list.Add(-1); // Padding for non-contiguous appends
                            }

                            list.Add(value);
                            writeIndex++;
                        }
                    }
                }
            }
        }

        private enum MergeModes
        {
            Append,
            Htp,
            Ltp,
            FailOver,
            Average
        }
    }
}