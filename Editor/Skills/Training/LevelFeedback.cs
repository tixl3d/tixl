#nullable enable
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
        _relevancyByKey.Clear();

        var compositionUi = _composition.GetSymbolUi();

        var childIndex = 0;
        foreach (var (childId, childInstance) in _composition.Children)
        {
            // Snapshots only capture children with EnabledForSnapshots == true (see
            // VariationHandling.AddSnapshotEnabledChildrenToList). Mirror that filter here,
            // otherwise non-default values on scaffolding ops would all read as Forbidden
            // immediately after loading the level.
            if (!compositionUi.ChildUis.TryGetValue(childId, out var childUi) || !childUi.EnabledForSnapshots)
                continue;

            _solution.ParameterSetsForChildIds.TryGetValue(childId, out var solutionParams);

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
                if (!ValueUtils.BlendMethods.ContainsKey(input.Value.ValueType))
                {
                    paramIndex++;
                    continue;
                }

                // Gradient (and similar structured reference types) have no reliable equality
                // — they fall back to ToString comparison which produces phantom mismatches
                // even for visually identical gradients. Skip them until a proper sample-based
                // comparison is added.
                if (input.Value.ValueType == typeof(Gradient))
                {
                    paramIndex++;
                    continue;
                }

                var inputId = input.InputDefinition.Id;
                InputValue? solutionValue = null;
                if (solutionParams != null)
                    solutionParams.TryGetValue(inputId, out solutionValue);
                var inSolution = solutionValue != null;

                // Classify by value equality, not the IsDefault flag. Toggling a bool back to
                // its default value (or typing the default into a numeric field) leaves
                // IsDefault=false but the parameter is effectively untouched — and should not
                // linger as Forbidden after the user has corrected it.
                var isEffectivelyDefault = AreEqual(input.Value, input.DefaultValue);

                ParamState state;
                if (isEffectivelyDefault)
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
                {
                    paramIndex++;
                    continue; // dominant case; skip storage to keep the dict small
                }

                var key = new Key(childId, inputId);
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

        if (!_solution.ParameterSetsForChildIds.TryGetValue(childId, out var solutionParams))
            return 0f;

        if (!solutionParams.TryGetValue(inputId, out var solutionValue))
            return 0f;

        if (!_composition.Children.TryGetChildInstance(childId, out var childInstance))
            return 0f;

        foreach (var inputSlot in childInstance.Inputs)
        {
            var input = inputSlot.Input;
            if (input == null || input.InputDefinition.Id != inputId)
                continue;

            return ComputeProximity(input.Value, solutionValue, input.DefaultValue);
        }

        return 0f;
    }

    private static float ComputeProximity(InputValue current, InputValue solution, InputValue defaultValue)
    {
        // Discrete and unsupported types: no useful arc. Signal "skip" with a negative
        // sentinel so the renderer can omit both the background ring and the progress fill.
        if (!SupportsProximityArc(current.ValueType))
            return -1f;

        var distCurToSol = Distance(current, solution);
        if (distCurToSol < 0)
            return -1f;

        var distDefToSol = Distance(defaultValue, solution);
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

    private const double HintDelaySeconds = 5.0;
    private const double HintFadeInSeconds = 5.0;

    private readonly Instance _composition;
    private readonly Variation _solution;
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
