#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Logging;
using T3.Core.Operator.Slots;
using T3.Serialization;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using Int3 = T3.Core.DataTypes.Vector.Int3;

namespace T3.Core.Operator;

public class SymbolExtension { }

public sealed class Animator : SymbolExtension
{
    // Stores all animation curves as [childId][inputId] -> array of curves
    private readonly Dictionary<Guid, Dictionary<Guid, Curve[]>> _curvesByChildAndInput = new();

    public void CopyAnimationsTo(
        Animator targetAnimator,
        List<Guid> childrenToCopyFrom,
        Dictionary<Guid, Guid> oldToNewIdDict)
    {
        // Collect first, insert after: when copying within one composition (e.g. splitting a clip),
        // targetAnimator is THIS animator — inserting while enumerating throws EnumFailedVersion.
        var pendingCopies = new List<(Guid NewChildId, Guid InputId, List<Curve> Curves)>();
        foreach (var (oldChildId, inputDict) in _curvesByChildAndInput)
        {
            if (!childrenToCopyFrom.Contains(oldChildId))
                continue;

            if (!oldToNewIdDict.TryGetValue(oldChildId, out var newChildId))
                continue;

            foreach (var (inputId, curves) in inputDict)
            {
                if (curves.Length == 0)
                    continue;

                var cloned = new List<Curve>(curves.Length);
                for (var i = 0; i < curves.Length; i++)
                {
                    cloned.Add(curves[i].TypedClone());
                }

                pendingCopies.Add((newChildId, inputId, cloned));
            }
        }

        foreach (var (newChildId, inputId, curves) in pendingCopies)
        {
            targetAnimator.AddCurvesToInput(curves, newChildId, inputId);
        }
    }

    public void RemoveAnimationsFromInstances(List<Guid> instanceIds)
    {
        foreach (var childId in instanceIds)
            _curvesByChildAndInput.Remove(childId);
    }

    /// <summary>
    /// Converts the global playback time into the local time an animated input of <paramref name="instance"/>
    /// is sampled at, by composing the time-clip remaps of the instance and its ancestors (outermost first).
    /// Keys must be inserted in this space — curves are read at <c>context.LocalTime</c>, which arrives
    /// remapped. Identity when no enclosing clip remaps (including instances without a parent path).
    /// </summary>
    public static double GetLocalAnimationTime(Instance? instance, double globalTimeInBars)
    {
        if (instance == null)
            return globalTimeInBars;

        var time = GetLocalAnimationTime(instance.Parent, globalTimeInBars);

        var outputs = instance.Outputs;
        for (var i = 0; i < outputs.Count; i++)
        {
            if (outputs[i] is ITimeClipProvider clipProvider)
            {
                time = clipProvider.TimeClip.MapTimelineToSource(time);
                break;
            }
        }

        return time;
    }

    /// <summary>
    /// Inverse of <see cref="GetLocalAnimationTime"/>: maps a local animation time (e.g. a keyframe's U)
    /// of <paramref name="instance"/> back to the global playback time it takes effect at.
    /// </summary>
    public static double GetGlobalAnimationTime(Instance? instance, double localTimeInBars)
    {
        if (instance == null)
            return localTimeInBars;

        var time = localTimeInBars;
        var outputs = instance.Outputs;
        for (var i = 0; i < outputs.Count; i++)
        {
            if (outputs[i] is ITimeClipProvider clipProvider)
            {
                time = clipProvider.TimeClip.MapSourceToTimeline(time);
                break;
            }
        }

        return GetGlobalAnimationTime(instance.Parent, time);
    }

    /// <summary>
    /// The local-time window that corresponds to ~1/100 bar of playback time at <paramref name="instance"/> —
    /// the tolerance for treating a key as "at" the playhead. Exact-equality lookups fail once a time clip
    /// remaps: the clip's float ranges make the mapped playhead land fractionally off a key's quantized U.
    /// </summary>
    public static double GetLocalTimeTolerance(Instance? instance, double globalTimeInBars)
    {
        var localTime = GetLocalAnimationTime(instance, globalTimeInBars);
        var localTimeShifted = GetLocalAnimationTime(instance, globalTimeInBars + 0.01);
        return Math.Max(0.0001, Math.Abs(localTimeShifted - localTime));
    }

    /// <summary>
    /// If any curve of the given input has a key within <paramref name="tolerance"/> of
    /// <paramref name="localTime"/>, returns that key's exact time — so edits update the existing key
    /// instead of inserting a fractionally-offset duplicate next to it.
    /// </summary>
    public double SnapToExistingKeyTime(Guid childId, Guid inputId, double localTime, double tolerance)
    {
        if (!_curvesByChildAndInput.TryGetValue(childId, out var inputDict)
            || !inputDict.TryGetValue(inputId, out var curves))
            return localTime;

        var bestTime = localTime;
        var bestDistance = tolerance;
        foreach (var curve in curves)
        {
            if (!curve.TryGetPreviousKey(localTime + tolerance, out var key))
                continue;

            var distance = Math.Abs(key.U - localTime);
            if (distance > bestDistance)
                continue;

            bestDistance = distance;
            bestTime = key.U;
        }

        return bestTime;
    }

    public Curve[]? AddOrRestoreCurvesToInput(IInputSlot inputSlot, Curve[]? originalCurves)
    {
        switch (inputSlot)
        {
            case Slot<float> floatInputSlot:
                return AddCurvesForFloatVector(inputSlot, [floatInputSlot.Value], originalCurves);

            case Slot<Vector2> v2Slot:
                return AddCurvesForFloatVector(inputSlot, v2Slot.Value.ToArray(), originalCurves);

            case Slot<Vector3> v3Slot:
                return AddCurvesForFloatVector(inputSlot, v3Slot.Value.ToArray(), originalCurves);

            case Slot<Vector4> v4Slot:
                return AddCurvesForFloatVector(inputSlot, v4Slot.Value.ToArray(), originalCurves);

            case Slot<int> intSlot:
                return AddCurvesForIntVector(inputSlot, [intSlot.Value], originalCurves);

            case Slot<Int2> size2Slot:
                return AddCurvesForIntVector(inputSlot,
                                                [size2Slot.Value.Width, size2Slot.Value.Height],
                                            originalCurves);

            case Slot<bool> boolSlot:
                return AddCurvesForIntVector(inputSlot,
                                                [boolSlot.Value ? 1 : 0],
                                            originalCurves);
        }

        Log.Error("Could not create curves for this type");
        return null;
    }

    private Curve[] AddCurvesForFloatVector(IInputSlot inputSlot, float[] values, Curve[]? originalCurves)
    {
        var childId = inputSlot.Parent.SymbolChildId;
        var inputId = inputSlot.Id;
        var now = GetLocalAnimationTime(inputSlot.Parent, Playback.Current.TimeInBars);

        var curves = originalCurves ?? new Curve[values.Length];
        if (curves.Length != values.Length)
        {
            Array.Resize(ref curves, values.Length);
        }

        for (var i = 0; i < values.Length; i++)
        {
            
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            curves[i] ??= new Curve();
            curves[i]!.AddOrUpdateV(now,
                                   new VDefinition
                                       {
                                           Value = values[i],
                                           InInterpolation = VDefinition.KeyInterpolation.Linear,
                                           OutInterpolation = VDefinition.KeyInterpolation.Linear,
                                           BrokenTangents = true,
                                       });
        }

        SetCurveArray(childId, inputId, curves);
        return curves;
    }

    private Curve[] AddCurvesForIntVector(IInputSlot inputSlot, int[] values, Curve[]? originalCurves)
    {
        var childId = inputSlot.Parent.SymbolChildId;
        var inputId = inputSlot.Id;
        var now = GetLocalAnimationTime(inputSlot.Parent, Playback.Current.TimeInBars);

        var curves = originalCurves ?? new Curve[values.Length];
        if (curves.Length != values.Length)
        {
            Array.Resize(ref curves, values.Length);
        }

        for (var i = 0; i < values.Length; i++)
        {
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            curves[i] ??= new Curve();
            
            curves[i].AddOrUpdateV(now,
                                   new VDefinition
                                       {
                                           Value = values[i],
                                           InInterpolation = VDefinition.KeyInterpolation.Constant,
                                           OutInterpolation = VDefinition.KeyInterpolation.Constant,
                                       });
        }

        SetCurveArray(childId, inputId, curves);
        return curves;
    }

    internal void CreateUpdateActionsForExistingCurves(IEnumerable<Instance> childInstances)
    {
        //var sortedChildIds = _curvesByChildAndInput.Keys.OrderBy(id => id);
            
        foreach (var instance in childInstances)
        {
            var childId = instance.SymbolChildId;
            // if(!childInstances.TryGetValue(childId, out var instance))
            //     continue;
            if(!_curvesByChildAndInput.TryGetValue(childId, out var inputsDict))
                continue;

            //var inputsDict = _curvesByChildAndInput[childId];
            foreach (var inputId in inputsDict.Keys.OrderBy(id => id))
            {
                var curves = inputsDict[inputId];
                if (curves.Length == 0)
                    continue;

                var inputSlot = instance.Inputs.FirstOrDefault(i => i.Id == inputId);
                if (inputSlot == null)
                    continue;

                switch (curves.Length)
                {
                    case 1:
                        switch (inputSlot)
                        {
                            case Slot<float> fSlot:
                                fSlot.OverrideWithAnimationAction(ctx =>
                                                                      fSlot.Value = (float)curves[0].GetSampledValue(ctx.LocalTime));
                                fSlot.DirtyFlag.Trigger |= DirtyFlagTrigger.Animated;
                                break;

                            case Slot<int> iSlot:
                                iSlot.OverrideWithAnimationAction(ctx =>
                                                                      iSlot.Value = (int)curves[0].GetSampledValue(ctx.LocalTime));
                                iSlot.DirtyFlag.Trigger |= DirtyFlagTrigger.Animated;
                                break;

                            case Slot<bool> bSlot:
                                bSlot.OverrideWithAnimationAction(ctx =>
                                                                      bSlot.Value = curves[0].GetSampledValue(ctx.LocalTime) > 0.5f);
                                bSlot.DirtyFlag.Trigger |= DirtyFlagTrigger.Animated;
                                break;
                        }
                        break;

                    case 2:
                        var slot2 = inputSlot;
                        switch (slot2)
                        {
                            case Slot<System.Numerics.Vector2> v2Slot:
                                v2Slot.OverrideWithAnimationAction(ctx =>
                                                                   {
                                                                       v2Slot.Value.X = (float)curves[0].GetSampledValue(ctx.LocalTime);
                                                                       v2Slot.Value.Y = (float)curves[1].GetSampledValue(ctx.LocalTime);
                                                                   });
                                v2Slot.DirtyFlag.Trigger |= DirtyFlagTrigger.Animated;
                                break;

                            case Slot<Int2> size2Slot:
                                size2Slot.OverrideWithAnimationAction(ctx =>
                                                                      {
                                                                          size2Slot.Value.Width = (int)curves[0].GetSampledValue(ctx.LocalTime);
                                                                          size2Slot.Value.Height = (int)curves[1].GetSampledValue(ctx.LocalTime);
                                                                      });
                                size2Slot.DirtyFlag.Trigger |= DirtyFlagTrigger.Animated;
                                break;
                        }
                        break;

                    case 3:
                        var slot3 = inputSlot;
                        switch (slot3)
                        {
                            case Slot<System.Numerics.Vector3> v3Slot:
                                v3Slot.OverrideWithAnimationAction(ctx =>
                                                                   {
                                                                       v3Slot.Value.X = (float)curves[0].GetSampledValue(ctx.LocalTime);
                                                                       v3Slot.Value.Y = (float)curves[1].GetSampledValue(ctx.LocalTime);
                                                                       v3Slot.Value.Z = (float)curves[2].GetSampledValue(ctx.LocalTime);
                                                                   });
                                v3Slot.DirtyFlag.Trigger |= DirtyFlagTrigger.Animated;
                                break;

                            case Slot<Int3> i3Slot:
                                i3Slot.OverrideWithAnimationAction(ctx =>
                                                                   {
                                                                       i3Slot.Value.X = (int)curves[0].GetSampledValue(ctx.LocalTime);
                                                                       i3Slot.Value.Y = (int)curves[1].GetSampledValue(ctx.LocalTime);
                                                                       i3Slot.Value.Z = (int)curves[2].GetSampledValue(ctx.LocalTime);
                                                                   });
                                i3Slot.DirtyFlag.Trigger |= DirtyFlagTrigger.Animated;
                                break;
                        }
                        break;

                    case 4:
                        if (inputSlot is Slot<System.Numerics.Vector4> v4Slot)
                        {
                            v4Slot.OverrideWithAnimationAction(ctx =>
                                                               {
                                                                   v4Slot.Value.X = (float)curves[0].GetSampledValue(ctx.LocalTime);
                                                                   v4Slot.Value.Y = (float)curves[1].GetSampledValue(ctx.LocalTime);
                                                                   v4Slot.Value.Z = (float)curves[2].GetSampledValue(ctx.LocalTime);
                                                                   v4Slot.Value.W = (float)curves[3].GetSampledValue(ctx.LocalTime);
                                                               });
                            v4Slot.DirtyFlag.Trigger |= DirtyFlagTrigger.Animated;
                        }
                        break;

                    default:
                        Debug.Assert(false);
                        break;
                }
            }
        }
    }

    public void RemoveAnimationFrom(IInputSlot inputSlot)
    {
        inputSlot.RestoreUpdateAction();
        inputSlot.DirtyFlag.Trigger &= ~DirtyFlagTrigger.Animated;

        var childId = inputSlot.Parent.SymbolChildId;
        var inputId = inputSlot.Id;
        if (_curvesByChildAndInput.TryGetValue(childId, out var inputDict))
        {
            inputDict.Remove(inputId);
            if (inputDict.Count == 0)
                _curvesByChildAndInput.Remove(childId);
        }
    }

    public bool TryGetFirstInputAnimationCurve(IInputSlot inputSlot, [NotNullWhen(true)] out Curve? curve)
    {
        curve = null;
        var childId = inputSlot.Parent.SymbolChildId;
        var inputId = inputSlot.Id;
        if (_curvesByChildAndInput.TryGetValue(childId, out var inputDict)
            && inputDict.TryGetValue(inputId, out var curves)
            && curves.Length > 0)
        {
            curve = curves[0];
            return true;
        }
        return false;
    }

    public bool IsInputSlotAnimated(IInputSlot inputSlot)
    {
        var childId = inputSlot.Parent.SymbolChildId;
        var inputId = inputSlot.Id;
        return _curvesByChildAndInput.TryGetValue(childId, out var inputDict)
               && inputDict.ContainsKey(inputId);
    }

    public bool IsInputAnimated(Symbol.Child child, Symbol.Child.Input input)
    {
        return IsAnimated(child.Id, input.Id);
    }

    public bool IsAnimated(Guid childId, Guid inputId)
    {
        return _curvesByChildAndInput.TryGetValue(childId, out var inputDict)
               && inputDict.ContainsKey(inputId);
    }

    public bool IsInstanceAnimated(Instance instance)
    {
        return _curvesByChildAndInput.ContainsKey(instance.SymbolChildId);
    }

    public bool TryGetCurvesForChildInput(Guid childId, Guid inputId,  [NotNullWhen(true)] out Curve[]? curves)
    {
        curves = null;
        return _curvesByChildAndInput.TryGetValue(childId, out var inputDict)
               && inputDict.TryGetValue(inputId, out  curves)
               && curves.Length >0;
    }

    public bool TryGetCurvesForInputSlot(IInputSlot inputSlot,  [NotNullWhen(true)] out Curve[]? curves)
    {
        return TryGetCurvesForChildInput(inputSlot.Parent.SymbolChildId, inputSlot.Id, out curves);
    }

    /// <summary>Retimes all keys of all animated inputs of one child by <paramref name="factor"/>.</summary>
    public void ScaleKeyTimesOfChild(Guid childId, double factor)
    {
        if (!_curvesByChildAndInput.TryGetValue(childId, out var inputDict))
            return;

        foreach (var curves in inputDict.Values)
        {
            foreach (var curve in curves)
            {
                curve.ScaleKeyTimes(factor);
            }
        }
    }

    public IEnumerable<VDefinition?> GetTimeKeys(Guid childId, Guid inputId, double time)
    {
        if (!TryGetCurvesForChildInput(childId, inputId, out var curves))
            yield break;
            
        foreach (var curve in curves)
        {
            yield return curve.GetV(time); // Note: Can in include Null!
        }
    }

    public void SetTimeKeys(Guid childId, Guid inputId, double time, List<VDefinition?> vDefinitions)
    {
        if (!TryGetCurvesForChildInput(childId, inputId, out var curves))
            return;
            
        for (var i = 0; i < curves.Length; i++)
        {
            var vDef = vDefinitions[i];
            if (vDef == null)
                curves[i].RemoveKeyframeAt(time);
            else
                curves[i].AddOrUpdateV(time, vDef);
        }
    }

    internal void Write(JsonTextWriter writer)
    {
        if (_curvesByChildAndInput.Count == 0)
            return;

        // Flatten everything into a list (childId, inputId, index, curve).
        var allCurves = new List<(Guid childId, Guid inputId, int index, Curve curve)>();
        foreach (var (childId, inputDict) in _curvesByChildAndInput)
        {
            foreach (var (inputId, curveArr) in inputDict)
            {
                for (int i = 0; i < curveArr.Length; i++)
                {
                    allCurves.Add((childId, inputId, i, curveArr[i]));
                }
            }
        }

        // Sort by index only, matching the original behavior of
        //    .OrderBy(valuePair => valuePair.Key.Index)
        // so channels are grouped in a stable order (0,1,2,3, etc.)
        var ordered = allCurves.OrderBy(x => x.index).ToList();

        writer.WritePropertyName("Animator");
        writer.WriteStartArray();
        foreach (var (childId, inputId, index, curve) in ordered)
        {
            writer.WriteStartObject();
            writer.WriteValue("InstanceId", childId);
            writer.WriteValue("InputId", inputId);
            if (index != 0)
                writer.WriteValue("Index", index);

            curve.Write(writer);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
        

    internal void Read(JToken inputToken, Symbol symbol)
    {
        // Build a local list so we can populate the dictionary afterwards
        var buffer = new List<(Guid childId, Guid inputId, int index, Curve curve)>();

        foreach (var entry in inputToken)
        {
            JsonUtils.TryGetGuid(entry["InstanceId"], out var childId);
            JsonUtils.TryGetGuid(entry["InputId"], out var inputId);

            var indexToken = entry.SelectToken("Index");
            var index = indexToken?.Value<int>() ?? 0;

            if (!symbol.Children.ContainsKey(childId))
                continue;

            var curve = new Curve();
            curve.Read(entry);

            buffer.Add((childId, inputId, index, curve));
        }

        // Sort by index so multi-channel arrays come in sorted order
        var sorted = buffer.OrderBy(b => b.index).ToList();
        foreach (var group in sorted.GroupBy(x => (x.childId, x.inputId)))
        {
            var maxIndex = group.Max(g => g.index);
            var curveArray = new Curve[maxIndex + 1];
            foreach (var g in group)
                curveArray[g.index] = g.curve;

            SetCurveArray(group.Key.childId, group.Key.inputId, curveArray);
        }
    }

    public void AddCurvesToInput(List<Curve> curves, IInputSlot inputSlot)
    {
        if (inputSlot.Parent?.Parent == null)
        {
            Log.Warning("Can't add animation curves without valid structure.");
            return;
        }

        var childId = inputSlot.Parent.SymbolChildId;
        var inputId = inputSlot.Id;
        SetCurveArray(childId, inputId, curves.ToArray());

        inputSlot.Parent.Parent.Symbol.CreateOrUpdateActionsForAnimatedChildren();
    }

    // Overload without needing a real inputSlot (used in CopyAnimationsTo)
    public void AddCurvesToInput(List<Curve> curves, Guid childId, Guid inputId)
    {
        SetCurveArray(childId, inputId, curves.ToArray());
    }

    private void SetCurveArray(Guid childId, Guid inputId, Curve[] curves)
    {
        if (!_curvesByChildAndInput.TryGetValue(childId, out var inputDict))
        {
            inputDict = new Dictionary<Guid, Curve[]>();
            _curvesByChildAndInput[childId] = inputDict;
        }
        inputDict[inputId] = curves;
    }

    public static void UpdateVector3InputValue(InputSlot<System.Numerics.Vector3> inputSlot, Vector3 value)
    {
        if (inputSlot.Parent?.Parent == null)
        {
            Log.Warning("Can't add animation curves without valid structure.");
            return;
        }

        var animator = inputSlot.Parent.Parent.Symbol.Animator;
        if (animator.IsInputSlotAnimated(inputSlot))
        {
            if (!animator.TryGetCurvesForInputSlot(inputSlot, out var curves))
                return;

            var time = GetLocalAnimationTime(inputSlot.Parent, Playback.Current.TimeInBars);
            for (var i = 0; i < 3; i++)
            {
                var vDef = curves[i].GetV(time) ?? new VDefinition { U = time };
                vDef.Value = value.GetValueUnsafe(i);
                curves[i].AddOrUpdateV(time, vDef);
            }
        }
        else
        {
            inputSlot.SetTypedInputValue(value);
        }
    }

    public static void UpdateFloatInputValue(InputSlot<float> inputSlot, float value)
    {
        if (inputSlot.Parent?.Parent == null)
        {
            Log.Warning("Can't add animation curves without valid structure.");
            return;
        }

        if (inputSlot.Parent.Parent.Symbol
                     .Animator
                     .TryGetCurvesForInputSlot(inputSlot, out var curves))
        {
            var firstCurve = curves[0];
            var time = GetLocalAnimationTime(inputSlot.Parent, Playback.Current.TimeInBars);
            var vDef = firstCurve.GetV(time) ?? new VDefinition { U = time };
            vDef.Value = value;
            firstCurve.AddOrUpdateV(time, vDef);
        }
        else
        {
            inputSlot.SetTypedInputValue(value);
        }
    }
}