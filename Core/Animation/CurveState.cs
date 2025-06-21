#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Serialization;

namespace T3.Core.Animation;

internal sealed class CurveState
{
    internal SortedList<double, VDefinition> Table { get; set; }

    internal CurveUtils.OutsideCurveBehavior PreCurveMapping
    {
        get => _preCurveMapping;
        set
        {
            _preCurveMapping = value;
            PreCurveMapper = CurveUtils.CreateOutsideCurveMapper(value);
        }
    }

    internal CurveUtils.OutsideCurveBehavior PostCurveMapping
    {
        get => _postCurveMapping;
        set
        {
            _postCurveMapping = value;
            PostCurveMapper = CurveUtils.CreateOutsideCurveMapper(value);
        }
    }

    internal IOutsideCurveMapper? PreCurveMapper { get; private set; }
    internal IOutsideCurveMapper? PostCurveMapper { get; private set; }

    internal CurveState()
    {
        Table = new SortedList<double, VDefinition>();
        PreCurveMapping = CurveUtils.OutsideCurveBehavior.Constant;
        PostCurveMapping = CurveUtils.OutsideCurveBehavior.Constant;
    }

    internal CurveState Clone()
    {
        var clone = new CurveState { PreCurveMapping = _preCurveMapping, PostCurveMapping = _postCurveMapping };

        foreach (var point in Table)
            clone.Table[point.Key] = point.Value.Clone();

        return clone;
    }

    internal void Write(JsonTextWriter writer)
    {
        lock (this)
        {
            writer.WritePropertyName("Curve");
            writer.WriteStartObject();

            writer.WriteObject("PreCurve", PreCurveMapping);
            writer.WriteObject("PostCurve", PostCurveMapping);

            // write keys
            writer.WritePropertyName("Keys");
            writer.WriteStartArray();

            foreach (var point in Table)
            {
                writer.WriteStartObject();

                writer.WriteValue("Time", point.Key);
                point.Value.Write(writer);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteEndObject();
        }
    }

    internal void Read(JToken inputToken)
    {
        var curveToken = inputToken["Curve"];
        if (curveToken == null)
            return;

        PreCurveMapping = curveToken["PreCurve"].GetEnumValue<CurveUtils.OutsideCurveBehavior>();
        PostCurveMapping = curveToken["PostCurve"].GetEnumValue<CurveUtils.OutsideCurveBehavior>();

        if (curveToken["Keys"] is not JArray array)
            return;

        foreach (var keyEntry in array)
        {
            var time = keyEntry.Value<double>("Time");
            time = Math.Round(time, Curve.TIME_PRECISION);
            var key = new VDefinition();
            key.Read(keyEntry);
            key.U = time;
            Table.Add(time, key);
        }
    }

    private CurveUtils.OutsideCurveBehavior _preCurveMapping;
    private CurveUtils.OutsideCurveBehavior _postCurveMapping;
}