using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Animation;
using T3.Core.Logging;

namespace T3.Core.DataTypes;

public class Curve : IEditableInputType
{
    public static readonly int TIME_PRECISION = 4;

    public object Clone()
    {
        return TypedClone();
    }

    public Curve TypedClone()
    {
        return new Curve { _state = _state.Clone() };
    }

    public Animation.CurveUtils.OutsideCurveBehavior PreCurveMapping
    {
        get => _state.PreCurveMapping;
        set => _state.PreCurveMapping = value;
    }

    public Animation.CurveUtils.OutsideCurveBehavior PostCurveMapping
    {
        get => _state.PostCurveMapping;
        set => _state.PostCurveMapping = value;
    }

    public bool HasVAt(double u)
    {
        u = Math.Round(u, TIME_PRECISION);
        return _state.Table.ContainsKey(u);
    }

    public int CompareTo(object obj)
    {
        if (obj.GetHashCode() < GetHashCode())
        {
            return -1;
        }
        else if (obj.GetHashCode() > GetHashCode())
        {
            return 1;
        }
        return 0;
    }

    public bool HasKeyBefore(double u)
    {
        if (_state.Table.Count == 0)
            return false;
            
        return _state.Table.Keys[0] < Math.Round(u, TIME_PRECISION);
    }


    public bool HasKeyAfter(double u)
    {
        if (_state.Table.Count == 0)
            return false;
            
        return _state.Table.Keys[ _state.Table.Count-1] > Math.Round(u, TIME_PRECISION);
    }

    public bool TryGetPreviousKey(double u, out VDefinition key)
    {
        u = Math.Round(u, TIME_PRECISION);
        // todo: Refactor to avoid linq and use binary search
        (_, key) = _state.Table.LastOrDefault(e => e.Key < u);
        return key != null;
    }
        
    public bool TryGetNextKey(double u, out VDefinition key)
    {
        u = Math.Round(u, TIME_PRECISION);
        // todo: Refactor to avoid linq and use binary search
        (_, key) = _state.Table.FirstOrDefault(e => e.Key > u);
        return key != null;
    }
        

    public void AddOrUpdateV(double u, VDefinition key)
    {
        u = Math.Round(u, TIME_PRECISION);
        key.U = u;
        _state.Table[u] = key;
        SplineInterpolator.UpdateTangents(_state.Table.ToList());
    }

    public void RemoveKeyframeAt(double u)
    {
        u = Math.Round(u, TIME_PRECISION);
        var state = _state;
        state.Table.Remove(u);
        SplineInterpolator.UpdateTangents(state.Table.ToList());
    }

    public void UpdateTangents()
    {
        SplineInterpolator.UpdateTangents(_state.Table.ToList());
    }


    /// <summary>
    /// Tries to move a keyframe to a new position
    /// </summary>
    /// <returns>Returns false if the position is already taken by a keyframe</returns>
    public void MoveKey(double u, double newU)
    {
        u = Math.Round(u, TIME_PRECISION);
        newU = Math.Round(newU, TIME_PRECISION);
        var state = _state;
        if (!state.Table.ContainsKey(u))
        {
            Log.Warning("Tried to move a non-existing keyframe from {u} to {newU}");
            return;
        }

        if (state.Table.ContainsKey(newU))
        {
            return;
        }

        var key = state.Table[u];
        state.Table.Remove(u);
        state.Table[newU] = key;
        key.U = newU;
        SplineInterpolator.UpdateTangents(state.Table.ToList());
    }

    public List<KeyValuePair<double, VDefinition>> GetPointTable()
    {
        var points = new List<KeyValuePair<double, VDefinition>>();
        foreach (var item in _state.Table)
        {
            //points.Add(new KeyValuePair<double, VDefinition>(item.Key, item.Value.Clone()));
            points.Add(new KeyValuePair<double, VDefinition>(item.Key, item.Value));
        }
        return points;
    }

    public IList<VDefinition> GetVDefinitions()
    {
        return _state.Table.Values;
    }

    // Returns null if there is no vDefinition at that position
    public VDefinition GetV(double u)
    {
        u = Math.Round(u, TIME_PRECISION);
        return _state.Table.TryGetValue(u, out var foundValue) 
                   ? foundValue.Clone() 
                   : null;
    }

    public double GetSampledValue(double u)
    {
        if (_state.Table.Count < 1 || double.IsNaN(u) || double.IsInfinity(u))
            return 0.0;

        u = Math.Round(u, TIME_PRECISION);
        double offset = 0.0;
        double mappedU = u;
        var first = _state.Table.First();
        var last = _state.Table.Last();

        if (u <= first.Key)
        {
            _state.PreCurveMapper.Calc(u, _state.Table, out mappedU, out offset);
        }
        else if (u >= last.Key)
        {
            _state.PostCurveMapper.Calc(u, _state.Table, out mappedU, out offset);
        }

        double resultValue = 0.0;
        if (mappedU <= first.Key)
        {
            resultValue = offset + first.Value.Value;
        }
        else if (mappedU >= last.Key)
        {
            resultValue = offset + last.Value.Value;
        }
        else
        {
            //interpolate
            var a = _state.Table.Last(e => e.Key <= mappedU);
            var b = _state.Table.First(e => e.Key > mappedU);

            if (a.Value.OutType == VDefinition.Interpolation.Constant)
            {
                resultValue = offset + ConstInterpolator.Interpolate(a, b, mappedU);
            }
            else if (a.Value.OutType == VDefinition.Interpolation.Linear && b.Value.OutType == VDefinition.Interpolation.Linear)
            {
                resultValue = offset + LinearInterpolator.Interpolate(a, b, mappedU);
            }
            else
            {
                resultValue = offset + SplineInterpolator.Interpolate(a, b, mappedU);
            }
        }

        return resultValue;
    }

    public virtual void Write(JsonTextWriter writer)
    {
        _state.Write(writer);
    }

    public virtual void Read(JToken inputToken)
    {
        _state.Read(inputToken);
    }

    private CurveState _state = new();

    public static void UpdateCurveBoolValue(Curve curves, double time, bool value)
    {
        var key = curves.GetV(time) ?? new VDefinition
                                           {
                                               U = time,
                                               InType = VDefinition.Interpolation.Constant,
                                               OutType = VDefinition.Interpolation.Constant,
                                               InEditMode = VDefinition.EditMode.Constant,
                                               OutEditMode = VDefinition.EditMode.Constant,
                                           };
        key.Value = value ? 1 : 0;
        curves.AddOrUpdateV(time, key);
    }

        
    public static void UpdateCurveValues(Curve[] curves, double time, float[] values)
    {
        for (var index = 0; index < curves.Length; index++)
        {
            var key = curves[index].GetV(time) ?? new VDefinition { U = time };
            key.Value = values[index];
            curves[index].AddOrUpdateV(time, key);
        }
    }
        
    public static void UpdateCurveValues(Curve[] curves, double time, int[] values)
    {
        for (var index = 0; index < curves.Length; index++)
        {
            var key = curves[index].GetV(time) ?? new VDefinition
                                                      {
                                                          U = time,
                                                          InType = VDefinition.Interpolation.Constant,
                                                          OutType = VDefinition.Interpolation.Constant,
                                                          InEditMode = VDefinition.EditMode.Constant,
                                                          OutEditMode = VDefinition.EditMode.Constant,                                                                   
                                                      };
            key.Value = values[index];
            curves[index].AddOrUpdateV(time, key);
        }
    }        
}