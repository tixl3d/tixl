#nullable enable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Serialization;

namespace T3.Core.Animation;

public sealed class VDefinition
{
    public enum Interpolation
    {
        Constant = 0,
        Linear,
        Spline,
    };

    public enum EditMode
    {
        Linear = 0,
        Smooth,
        Horizontal,
        Tangent,
        Constant,
        Cubic,
    }

    private double _u;
    public double U
    {
        get => _u; 
        set => _u = Math.Round(value, Curve.TIME_PRECISION);
    }
            
    public double Value { get; set; } = 0.0;
    public Interpolation InType { get; set; } = Interpolation.Linear;
    public Interpolation OutType { get; set; } = Interpolation.Linear;

    public EditMode InEditMode { get; set; } = EditMode.Linear;
    public EditMode OutEditMode { get; set; } = EditMode.Linear;

    public double InTangentAngle { get; set; }
    public double OutTangentAngle { get; set; }
    public bool Weighted { get; set; }
    public bool BrokenTangents { get; set; }

    public VDefinition Clone()
    {
        return new VDefinition()
                   {
                       Value = Value,
                       U = U,
                       InType = InType,
                       OutType = OutType,
                       InEditMode = InEditMode,
                       OutEditMode = OutEditMode,
                       InTangentAngle = InTangentAngle,
                       OutTangentAngle = OutTangentAngle
                   };
    }

    public void CopyValuesFrom(VDefinition def)
    {
        Value = def.Value;
        U = def.U;
        InType = def.InType;
        OutType = def.OutType;
        InEditMode = def.InEditMode;
        OutEditMode = def.OutEditMode;
        InTangentAngle = def.InTangentAngle;
        OutTangentAngle = def.OutTangentAngle;            
    }

    internal void Read(JToken jsonV)
    {
        Value = jsonV.Value<double>(nameof(Value));
        InType = jsonV[nameof(InType)].GetEnumValue(Interpolation.Linear);
        OutType = jsonV[nameof(OutType)].GetEnumValue(Interpolation.Linear);
        
        InTangentAngle = jsonV.Value<double>(nameof(InTangentAngle));
        OutTangentAngle = jsonV.Value<double>(nameof(OutTangentAngle));

        InEditMode = jsonV[nameof(InEditMode)].GetEnumValue(EditMode.Linear);
        OutEditMode = jsonV[nameof(OutEditMode)].GetEnumValue(EditMode.Linear);
    }

    internal void Write(JsonTextWriter writer)
    {
        writer.WriteValue(nameof(Value), Value);
        writer.WriteObject(nameof(InType), InType);
        writer.WriteObject(nameof(OutType), OutType);
        writer.WriteObject(nameof(InEditMode), InEditMode);
        writer.WriteObject(nameof(OutEditMode), OutEditMode);
        writer.WriteValue(nameof(InTangentAngle), InTangentAngle);
        writer.WriteValue(nameof(OutTangentAngle), OutTangentAngle);
    }
}