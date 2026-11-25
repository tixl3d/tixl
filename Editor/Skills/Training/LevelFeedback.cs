#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using ImGuiNET;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Core.Operator.Interfaces;
using T3.Core.Operator.Slots;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction.Variations;
using T3.Editor.Gui.Interaction.Variations.Model;
using T3.Editor.Gui.MagGraph.Interaction;
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
        Untouched, // current = level start, not in Solution
        Required,  // current = level start, in Solution
        Correct,   // current = Solution
        Warm,      // current ≠ Solution and ≠ level start, key ∈ Solution
        Forbidden, // current ≠ level start, key ∉ Solution
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
        _relevancyByKey.Clear();

        // All children are checked, not only the snapshot-enabled ones: snapshot control just marks
        // which parameters belong to the solution, while editing any other parameter is a mistake.
        var compositionUi = _composition.GetSymbolUi();
        var childIndex = 0;
        foreach (var (childId, childInstance) in _composition.Children)
        {
            compositionUi.ChildUis.TryGetValue(childId, out var childUi);

            var paramIndex = 0;
            foreach (var inputSlot in childInstance.Inputs)
            {
                var input = inputSlot.Input;
                if (input == null)
                {
                    paramIndex++;
                    continue;
                }

                // Mirror the type filter used by snapshot capture (SymbolVariationPool).
                // Non-blendable inputs can never appear in a snapshot, so they are out of scope.
                if (!IsComparable(input.Value.ValueType))
                {
                    paramIndex++;
                    continue;
                }

                var inputId = input.InputDefinition.Id;
                var key = new Key(childId, inputId);
                var startValue = GetStartValue(key, input);

                // A solution value equal to the start asks the player to leave the parameter alone,
                // so it is judged like any parameter outside the solution.
                var inSolution = TryGetSolutionValue(childId, childUi, input, out var solutionValue)
                                 && !AreEqual(solutionValue, startValue);

                // Classify by value equality, not the IsDefault flag: setting a value back to where
                // the level started counts as untouched, even though IsDefault may stay false.

                ParamState state;
                if (inSolution && AreEqual(input.Value, solutionValue!))
                {
                    state = ParamState.Correct;
                }
                else if (AreEqual(input.Value, startValue))
                {
                    state = inSolution ? ParamState.Required : ParamState.Untouched;
                }
                else
                {
                    state = inSolution ? ParamState.Warm : ParamState.Forbidden;
                }

                if (state == ParamState.Untouched)
                {
                    paramIndex++;
                    continue; // dominant case; skip storage to keep the dict small
                }

                _state[key] = state;
                _relevancyByKey[key] = ComputeRelevancy(paramIndex, childIndex, input.Value.ValueType);

                switch (state)
                {
                    case ParamState.Required:
                        IncrementCount(_requiredByChild, childId);
                        break;
                    case ParamState.Forbidden:
                        IncrementCount(_forbiddenByChild, childId);
                        break;
                }

                paramIndex++;
            }

            childIndex++;
        }

        UpdateFocusedTip();
        UpdateHintTimestamps();
        MaybeLogTransition();
    }

    /// <summary>
    /// Maintains <see cref="_hintSeenSince"/> so each visible hint has its own fade-in timer.
    /// Keys that stop being a visible hint (no longer Forbidden, no longer the focused tip)
    /// are dropped so a future re-appearance gets a fresh delay instead of snapping in.
    /// </summary>
    private void UpdateHintTimestamps()
    {
        _keysToRemove.Clear();
        foreach (var (key, _) in _hintSeenSince)
        {
            var stillVisible = (_state.TryGetValue(key, out var s) && s == ParamState.Forbidden)
                               || (_focusedTip.HasValue && _focusedTip.Value == key);
            if (!stillVisible)
                _keysToRemove.Add(key);
        }

        foreach (var key in _keysToRemove)
            _hintSeenSince.Remove(key);

        var now = ImGui.GetTime();
        foreach (var (key, s) in _state)
        {
            if (s == ParamState.Forbidden && !_hintSeenSince.ContainsKey(key))
                _hintSeenSince[key] = now;
        }

        if (_focusedTip is { } focused && !_hintSeenSince.ContainsKey(focused))
            _hintSeenSince[focused] = now;
    }

    /// <summary>
    /// Sort key for picking the focused tip among multiple candidates. Higher wins.
    /// Categorical tiers dominate position: a bool anywhere in the graph outranks any enum,
    /// which outranks any numeric/vector parameter. Within the same type tier, later
    /// children and later parameters win (closer to the visible output / closer to the
    /// effect the user is staring at).
    /// </summary>
    private static float ComputeRelevancy(int paramIndex, int childIndex, Type valueType)
    {
        var typeTier = 0f;
        if (valueType == typeof(bool))
            typeTier = 1000f;
        else if (valueType.IsEnum)
            typeTier = 500f;

        var positionScore = paramIndex * 0.01f + childIndex * 0.2f;
        return typeTier + positionScore;
    }

    /// <summary>
    /// Picks the single Required/Warm parameter to highlight as the "what's next" hint.
    /// Stickiness: keep the current pick as long as it remains Required or Warm; otherwise
    /// pick the highest-relevancy Warm (so a near-miss takes priority over an untouched
    /// parameter), falling back to the highest-relevancy Required.
    /// While the user is dragging *anything*, the focus is held even if the current tip
    /// momentarily reaches Correct — that lets users overshoot through the solution value
    /// without the tip vanishing mid-drag.
    /// </summary>
    private void UpdateFocusedTip()
    {
        if (_focusedTip.HasValue
            && _state.TryGetValue(_focusedTip.Value, out var current)
            && (current == ParamState.Warm || current == ParamState.Required))
        {
            return;
        }

        if (ImGui.IsAnyItemActive())
            return;

        _focusedTip = PickByRelevancy(ParamState.Warm) ?? PickByRelevancy(ParamState.Required);
    }

    private Key? PickByRelevancy(ParamState wanted)
    {
        Key? best = null;
        var bestScore = float.MinValue;
        foreach (var (key, s) in _state)
        {
            if (s != wanted)
                continue;

            var score = _relevancyByKey.TryGetValue(key, out var v) ? v : float.MinValue;
            if (score > bestScore)
            {
                bestScore = score;
                best = key;
            }
        }

        return best;
    }

    /// <summary>Returns true if this parameter is the currently focused "what's next" tip.</summary>
    public bool IsFocusedTip(Guid childId, Guid inputId)
    {
        return _focusedTip is { } key && key.ChildId == childId && key.InputId == inputId;
    }

    /// <summary>
    /// Proximity of the focused tip's current value to its solution value, normalized to
    /// [0, 1]. 0 = at default (no progress), 1 = at solution. Defined for numeric and vector
    /// types; bool/enum collapse to 0 or 1. Returns 0 for everything else (or when the
    /// parameter is not the focused tip). Lets the UI render a progress hint so the user
    /// can see their tweak is moving the value toward the target even when the rendered
    /// output is not yet responding.
    /// </summary>
    public float GetTipProximity(Guid childId, Guid inputId)
    {
        if (_focusedTip is not { } focused || focused.ChildId != childId || focused.InputId != inputId)
            return 0f;

        if (!_composition.Children.TryGetChildInstance(childId, out var childInstance))
            return 0f;

        _composition.GetSymbolUi().ChildUis.TryGetValue(childId, out var childUi);
        foreach (var inputSlot in childInstance.Inputs)
        {
            var input = inputSlot.Input;
            if (input == null || input.InputDefinition.Id != inputId)
                continue;

            if (!TryGetSolutionValue(childId, childUi, input, out var solutionValue))
                return 0f;

            return ComputeProximity(input.Value, solutionValue, GetStartValue(focused, input));
        }

        return 0f;
    }

    private static float ComputeProximity(InputValue current, InputValue solution, InputValue startValue)
    {
        // Discrete and unsupported types: no useful arc. Signal "skip" with a negative
        // sentinel so the renderer can omit both the background ring and the progress fill.
        if (!SupportsProximityArc(current.ValueType))
            return -1f;

        var distCurToSol = Distance(current, solution);
        if (distCurToSol < 0)
            return -1f;

        var distDefToSol = Distance(startValue, solution);
        if (distDefToSol <= 0f)
            return distCurToSol == 0f ? 1f : -1f;

        // Logarithmic remap normalized against the default→solution swing.
        //   At default (normalized = 1): progress = 0.5
        //   normalized = 0.1  (one decade closer): progress = 0.7
        //   normalized = 0.01 (two decades):       progress = 0.9
        //   normalized = 10x overshoot:            progress = 0.3
        // Small early progress is still visible, mid-range fills steadily, the final
        // approach eases as the user dials in the exact value.
        var scale = Math.Max(distDefToSol, 0.0001f);
        var normalized = distCurToSol / scale;
        if (normalized <= 0f)
            return 1f;

        var p = 0.5f - 0.2f * MathF.Log10(normalized);
        return Math.Clamp(p, 0f, 1f);
    }

    private static bool SupportsProximityArc(Type valueType)
    {
        if (valueType.IsEnum)
            return false;

        return valueType == typeof(float)
               || valueType == typeof(int)
               || valueType == typeof(Vector2)
               || valueType == typeof(Vector3)
               || valueType == typeof(Vector4);
    }

    /// <summary>Distance between two same-typed values. Negative result means the type is not supported.</summary>
    private static float Distance(InputValue a, InputValue b)
    {
        switch (a)
        {
            case InputValue<float> af when b is InputValue<float> bf:
                return MathF.Abs(af.Value - bf.Value);
            case InputValue<int> ai when b is InputValue<int> bi:
                return MathF.Abs(ai.Value - bi.Value);
            case InputValue<Vector2> a2 when b is InputValue<Vector2> b2:
                return Vector2.Distance(a2.Value, b2.Value);
            case InputValue<Vector3> a3 when b is InputValue<Vector3> b3:
                return Vector3.Distance(a3.Value, b3.Value);
            case InputValue<Vector4> a4 when b is InputValue<Vector4> b4:
                return Vector4.Distance(a4.Value, b4.Value);
            default:
                return -1f;
        }
    }

    /// <summary>
    /// Fade-in alpha for a specific parameter hint. Each visible hint has its own
    /// timestamp so newly-appearing blockers and freshly-picked tips each get the full
    /// delay+fade window, instead of snapping in just because the global timer has expired.
    /// </summary>
    public float GetHintAlpha(Guid childId, Guid inputId)
    {
        UpdateGlobalEligibility();
        return ComputeAlpha(new Key(childId, inputId));
    }

    /// <summary>
    /// Aggregate fade-in alpha for an op badge: max alpha among the keys that justify the
    /// badge at the given status level (all Forbidden children for Warning; just the
    /// focused tip for Tip).
    /// </summary>
    public float GetOpHintAlpha(Guid childId, IStatusProvider.StatusLevel level)
    {
        UpdateGlobalEligibility();

        if (level == IStatusProvider.StatusLevel.Warning)
        {
            var maxAlpha = 0f;
            foreach (var (key, s) in _state)
            {
                if (key.ChildId != childId || s != ParamState.Forbidden)
                    continue;

                var a = ComputeAlpha(key);
                if (a > maxAlpha)
                    maxAlpha = a;
            }

            return maxAlpha;
        }

        if (level == IStatusProvider.StatusLevel.Tip
            && _focusedTip is { } focused
            && focused.ChildId == childId)
        {
            return ComputeAlpha(focused);
        }

        return 0f;
    }

    private void UpdateGlobalEligibility()
    {
        if (TourInteraction.IsTourBlockingHints(_composition.GetSymbolUi()))
            _hintsEligibleSince = ImGui.GetTime();
    }

    private float ComputeAlpha(Key key)
    {
        if (!_hintSeenSince.TryGetValue(key, out var since))
            return 0f;

        var startedAt = Math.Max(since, _hintsEligibleSince);
        var elapsed = ImGui.GetTime() - startedAt;
        if (elapsed < HintDelaySeconds)
            return 0f;

        var progress = (float)((elapsed - HintDelaySeconds) / HintFadeInSeconds);
        return progress >= 1f ? 1f : progress;
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

        // Tip badge only surfaces the *focused* parameter's op, so the graph shows a single
        // forward-pointing hint. Other ops with Required/Warm parameters stay quiet.
        if (_focusedTip is { } focused && focused.ChildId == childId)
        {
            level = IStatusProvider.StatusLevel.Tip;
            message = _state.TryGetValue(focused, out var s) && s == ParamState.Warm
                          ? "You're close — keep tweaking the highlighted parameter…"
                          : "Try tweaking the highlighted parameter…";
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
        CaptureStartValues();

        var requiredKeyCount = 0;
        foreach (var (_, paramSet) in solution.ParameterSetsForChildIds)
            requiredKeyCount += paramSet.Count;

        // Temporary success log — remove once feedback is working end-to-end.
        Log.Info($"[LevelFeedback] Loaded solution snapshot '{solution.Title}' for level '{levelName}' "
                 + $"({solution.ParameterSetsForChildIds.Count} op(s), {requiredKeyCount} required parameter(s)).");
    }

    /// <summary>
    /// Records every comparable parameter as the level was authored. Edits are judged against this
    /// rather than the op defaults, so scaffolding values set by the level author don't read as
    /// mistakes.
    /// </summary>
    private void CaptureStartValues()
    {
        foreach (var (childId, childInstance) in _composition.Children)
        {
            foreach (var inputSlot in childInstance.Inputs)
            {
                var input = inputSlot.Input;
                if (input == null || !IsComparable(input.Value.ValueType))
                    continue;

                _startValues[new Key(childId, input.InputDefinition.Id)] = input.Value.Clone();
            }
        }
    }

    /// <summary>
    /// Snapshots don't store values that are at their default, and applying one resets the
    /// controlled parameters it doesn't store to their default. So for an op captured in the
    /// solution, a missing controlled input means its solution value is the default.
    /// </summary>
    private bool TryGetSolutionValue(Guid childId, SymbolUi.Child? childUi, Symbol.Child.Input input,
                                     [NotNullWhen(true)] out InputValue? solutionValue)
    {
        solutionValue = null;
        if (!_solution.ParameterSetsForChildIds.TryGetValue(childId, out var solutionParams))
            return false;

        var inputId = input.InputDefinition.Id;
        if (solutionParams.TryGetValue(inputId, out solutionValue))
            return true;

        if (childUi == null || !childUi.IsInputIncludedForVariation(inputId))
            return false;

        solutionValue = input.DefaultValue;
        return true;
    }

    /// <summary>Ops added while playing have no recorded start, so they start at their defaults.</summary>
    private InputValue GetStartValue(Key key, Symbol.Child.Input input)
    {
        return _startValues.TryGetValue(key, out var startValue) ? startValue : input.DefaultValue;
    }

    /// <summary>
    /// Mirrors the type filter of snapshot capture: non-blendable inputs can never be part of a
    /// solution. Gradients are skipped because they lack reliable equality — the ToString fallback
    /// reports phantom mismatches for identical gradients.
    /// </summary>
    private static bool IsComparable(Type valueType)
    {
        return ValueUtils.BlendMethods.ContainsKey(valueType) && valueType != typeof(Gradient);
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

    private const double HintDelaySeconds = 5.0;
    private const double HintFadeInSeconds = 5.0;

    private readonly Instance _composition;
    private readonly Variation _solution;
    private readonly Dictionary<Key, InputValue> _startValues = new(128);
    private readonly Dictionary<Key, ParamState> _state = new(64);
    private readonly Dictionary<Key, float> _relevancyByKey = new(64);
    private readonly Dictionary<Key, double> _hintSeenSince = new(16);
    private readonly List<Key> _keysToRemove = new(16);
    private readonly Dictionary<Guid, int> _forbiddenByChild = new(16);
    private readonly Dictionary<Guid, int> _requiredByChild = new(16);
    private readonly StringBuilder _logBuilder = new(64);
    private double _hintsEligibleSince = ImGui.GetTime();
    private Key? _focusedTip;
    private string? _lastLoggedFingerprint;
}
