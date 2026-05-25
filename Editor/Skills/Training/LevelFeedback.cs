#nullable enable
using System.Text;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.UiModel;

namespace T3.Editor.Skills.Training;

/// <summary>
/// Read-only per-frame analysis of how the user's edits compare to the level's solution snapshot.
/// One snapshot per level symbol is expected; absence or multiplicity is logged as a warning at load.
/// </summary>
internal sealed class LevelFeedback
{
    internal enum ParamState
    {
        Untouched, // current = default, not in Solution
        Required,  // current = default, in Solution
        Correct,   // current = Solution
        Warm,      // current ≠ Solution but key ∈ Solution
        Forbidden, // current ≠ default, key ∉ Solution
    }

    public static LevelFeedback? TryCreate(Instance compositionRoot)
    {
        var levelName = compositionRoot.Symbol.Name;
        var pool = VariationHandling.GetOrLoadVariations(compositionRoot.Symbol.Id);

        Variation? solution = null;
        var snapshotCount = 0;
        var presetCount = 0;
        foreach (var v in pool.AllVariations)
        {
            if (v.IsPreset)
            {
                presetCount++;
                continue;
            }

            snapshotCount++;
            if (solution == null)
                solution = v;
        }

        if (snapshotCount == 0)
        {
            if (presetCount > 0)
                Log.Warning($"No solution snapshot defined for level '{levelName}' (pool has {presetCount} preset(s) but no snapshots — presets are per-instance and don't qualify).");
            else
                Log.Warning($"No solution snapshot defined for level '{levelName}' (pool is empty).");
            return null;
        }

        if (snapshotCount > 1)
            Log.Warning($"More than one solution snapshot defined for level '{levelName}' ({snapshotCount} snapshots). Picking first.");

        return new LevelFeedback(compositionRoot, solution!, levelName);
    }

    public void Rebuild()
    {
        _state.Clear();
        _forbiddenByChild.Clear();
        _requiredByChild.Clear();

        var compositionUi = _composition.GetSymbolUi();

        foreach (var (childId, childInstance) in _composition.Children)
        {
            // Snapshots only capture children with EnabledForSnapshots == true (see
            // VariationHandling.AddSnapshotEnabledChildrenToList). Mirror that filter here,
            // otherwise non-default values on scaffolding ops would all read as Forbidden
            // immediately after loading the level.
            if (!compositionUi.ChildUis.TryGetValue(childId, out var childUi) || !childUi.EnabledForSnapshots)
                continue;

            _solution.ParameterSetsForChildIds.TryGetValue(childId, out var solutionParams);

            foreach (var inputSlot in childInstance.Inputs)
            {
                var input = inputSlot.Input;
                if (input == null)
                    continue;

                // Mirror the type filter used by snapshot capture (SymbolVariationPool).
                // Non-blendable inputs can never appear in a snapshot, so they are out of scope.
                if (!ValueUtils.BlendMethods.ContainsKey(input.Value.ValueType))
                    continue;

                var inputId = input.InputDefinition.Id;
                var isAtDefault = input.IsDefault;
                InputValue? solutionValue = null;
                if (solutionParams != null)
                    solutionParams.TryGetValue(inputId, out solutionValue);
                var inSolution = solutionValue != null;

                ParamState state;
                if (isAtDefault)
                {
                    state = inSolution ? ParamState.Required : ParamState.Untouched;
                }
                else if (inSolution)
                {
                    state = AreEqual(input.Value, solutionValue!) ? ParamState.Correct : ParamState.Warm;
                }
                else
                {
                    state = ParamState.Forbidden;
                }

                if (state == ParamState.Untouched)
                    continue; // dominant case; skip storage to keep the dict small

                _state[new Key(childId, inputId)] = state;

                switch (state)
                {
                    case ParamState.Required:
                        IncrementCount(_requiredByChild, childId);
                        break;
                    case ParamState.Forbidden:
                        IncrementCount(_forbiddenByChild, childId);
                        break;
                }
            }
        }

        MaybeLogTransition();
    }

    public bool TryGetParameterState(Guid childId, Guid inputId, out ParamState state)
    {
        return _state.TryGetValue(new Key(childId, inputId), out state);
    }

    public bool TryGetOpStatus(Guid childId, out IStatusProvider.StatusLevel level, out string? message)
    {
        if (_forbiddenByChild.TryGetValue(childId, out var forbiddenCount) && forbiddenCount > 0)
        {
            level = IStatusProvider.StatusLevel.Warning;
            message = forbiddenCount == 1
                          ? "This parameter is not part of the solution. Revert it."
                          : $"{forbiddenCount} parameters are not part of the solution. Revert them.";
            return true;
        }

        if (_requiredByChild.TryGetValue(childId, out var requiredCount) && requiredCount > 0)
        {
            level = IStatusProvider.StatusLevel.Tip;
            message = requiredCount == 1
                          ? "This operator has a parameter to change."
                          : $"{requiredCount} parameters here are still at their default.";
            return true;
        }

        level = IStatusProvider.StatusLevel.Undefined;
        message = null;
        return false;
    }

    private LevelFeedback(Instance composition, Variation solution, string levelName)
    {
        _composition = composition;
        _solution = solution;

        var requiredKeyCount = 0;
        foreach (var (_, paramSet) in solution.ParameterSetsForChildIds)
            requiredKeyCount += paramSet.Count;

        // Temporary success log — remove once feedback is working end-to-end.
        Log.Info($"[LevelFeedback] Loaded solution snapshot '{solution.Title}' for level '{levelName}' "
                 + $"({solution.ParameterSetsForChildIds.Count} op(s), {requiredKeyCount} required parameter(s)).");
    }

    private void MaybeLogTransition()
    {
        // Compact fingerprint, log only when it changes — gives observable behavior without per-frame spam.
        _logBuilder.Clear();
        _logBuilder.Append("R=").Append(CountState(ParamState.Required));
        _logBuilder.Append(" W=").Append(CountState(ParamState.Warm));
        _logBuilder.Append(" C=").Append(CountState(ParamState.Correct));
        _logBuilder.Append(" F=").Append(CountState(ParamState.Forbidden));

        var fingerprint = _logBuilder.ToString();
        if (fingerprint == _lastLoggedFingerprint)
            return;

        _lastLoggedFingerprint = fingerprint;
        Log.Debug($"[LevelFeedback] {fingerprint}");
    }

    private int CountState(ParamState state)
    {
        var count = 0;
        foreach (var (_, s) in _state)
        {
            if (s == state)
                count++;
        }

        return count;
    }

    private static void IncrementCount(Dictionary<Guid, int> dict, Guid key)
    {
        dict.TryGetValue(key, out var current);
        dict[key] = current + 1;
    }

    private static bool AreEqual(InputValue a, InputValue b)
    {
        if (a.ValueType != b.ValueType)
            return false;

        if (ValueUtils.CompareFunctions.TryGetValue(a.ValueType, out var compare))
            return compare(a, b);

        // Fallback for types not registered in ValueUtils.CompareFunctions.
        return a.ToString() == b.ToString();
    }

    private readonly record struct Key(Guid ChildId, Guid InputId);

    private readonly Instance _composition;
    private readonly Variation _solution;
    private readonly Dictionary<Key, ParamState> _state = new(64);
    private readonly Dictionary<Guid, int> _forbiddenByChild = new(16);
    private readonly Dictionary<Guid, int> _requiredByChild = new(16);
    private readonly StringBuilder _logBuilder = new(64);
    private string? _lastLoggedFingerprint;
}
