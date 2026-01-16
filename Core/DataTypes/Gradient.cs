using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Core.Logging;
using T3.Core.Utils;
using T3.Core.Utils.Splines;
using T3.Serialization;

namespace T3.Core.DataTypes;

public class Gradient : IEditableInputType
{
    public void Write(JsonTextWriter writer)
    {
        writer.WritePropertyName(nameof(Gradient));
        writer.WriteStartObject();

        writer.WriteObject("Interpolation", Interpolation);
        writer.WritePropertyName("Steps");
        writer.WriteStartArray();

        Step[] stepsToWrite;
        lock (Steps)
        {
            stepsToWrite = Steps.Select(step => new Step(step)).ToArray();
        }

        foreach (var step in stepsToWrite)
        {
            writer.WriteStartObject();
            writer.WriteObject("Id", step.Id);
            writer.WriteValue("NormalizedPosition", step.NormalizedPosition);

            writer.WritePropertyName("Color");
            writer.WriteStartObject();
            writer.WriteValue("R", step.Color.X);
            writer.WriteValue("G", step.Color.Y);
            writer.WriteValue("B", step.Color.Z);
            writer.WriteValue("A", step.Color.W);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public List<Step> Steps { get; set; } = new();
    public Interpolations Interpolation { get; set; }

    public virtual void Read(JToken inputToken)
    {
        Steps.Clear();
        JToken gradientToken = inputToken[typeof(Gradient).Name];
        if (gradientToken == null)
            return;

        try
        {
            if (gradientToken["Interpolation"] != null)
            {
                Interpolation = (Interpolations)Enum.Parse(typeof(Interpolations), gradientToken["Interpolation"].Value<string>());
            }

            var jArray = (JArray)gradientToken["Steps"];
            if (jArray == null) 
                return;
            
            foreach (var keyEntry in jArray)
            {
                JsonUtils.TryGetGuid(keyEntry["Id"], out var id);
                var colorToken = keyEntry["Color"];
                if (colorToken == null)
                    continue;

                Steps.Add(new Step
                              {
                                  NormalizedPosition = keyEntry.Value<float>("NormalizedPosition"),
                                  Id = id,
                                  Color = new Vector4(colorToken.Value<float>("R"),
                                                      colorToken.Value<float>("G"),
                                                      colorToken.Value<float>("B"),
                                                      colorToken.Value<float>("A")),
                              });
            }
        }
        catch (Exception e)
        {
            Log.Warning("Can't read gradient property " + e);
            if (Steps == null || Steps.Count < 1)
                Steps = CreateDefaultSteps();
        }
    }

    public object Clone() => TypedClone();

    public void SortHandles()
    {
        Steps.Sort((x, y) => x.NormalizedPosition.CompareTo(y.NormalizedPosition));
    }

    public Gradient TypedClone()
    {
        return new Gradient
                   {
                       Steps = Steps.Select(step => new Step
                                                        {
                                                            NormalizedPosition = step.NormalizedPosition,
                                                            Color = step.Color,
                                                            Id = Guid.NewGuid(),
                                                        })
                                    .ToList(),
                       Interpolation = Interpolation,
                   };
    }

    /// <remarks>
    /// this assumes that steps have been sorted by GradientEditor
    /// </remarks>
    public Vector4 Sample(float t)
    {
        t = t.Clamp(0, 1);
        Step previousStep = null;

        for (var i = 0; i < Steps.Count; i++)
        {
            var step = Steps[i];
            if (!(step.NormalizedPosition >= t))
            {
                previousStep = step;
                continue;
            }

            if (previousStep == null || previousStep.NormalizedPosition >= step.NormalizedPosition)
            {
                return step.Color;
            }

            if (Interpolation == Interpolations.Hold)
                return previousStep.Color;

            var fraction = t.RemapAndClamp(previousStep.NormalizedPosition, step.NormalizedPosition, 0, 1);

            switch (Interpolation)
            {
                case Interpolations.Linear:
                    break;

                case Interpolations.Smooth:
                    fraction = MathUtils.SmootherStep(0, 1, fraction);
                    break;

                case Interpolations.OkLab:
                    var vector4 = OkLab.Mix(previousStep.Color, step.Color, fraction);
                    Vector4.Max(vector4, Vector4.Zero);
                    return vector4;

                case Interpolations.Spline:
                    return SampleSpline(t);
            }

            return Vector4.Lerp(previousStep.Color, step.Color, fraction);
        }

        return previousStep?.Color ?? Vector4.One;
    }

    Vector4 SampleCatmullRomSpline(Vector4 p_mi1, Vector4 p_0, Vector4 p_1, Vector4 p_2, float t)
    {
        Vector4 a4 = p_0;
        Vector4 a3 = (p_1 - p_mi1) / 2.0f;
        Vector4 a1 = (p_2 - p_0) / 2.0f - 2.0f * p_1 + a3 + 2.0f * a4;
        Vector4 a2 = 3.0f * p_1 - (p_2 - p_0) / 2.0f - 2.0f * a3 - 3.0f * a4;

        return a1 * t * t * t + a2 * t * t + a3 * t + a4;
    }

    private static List<Step> CreateDefaultSteps()
    {
        return new List<Step>
                   {
                       new()
                           {
                               NormalizedPosition = 0,
                               Color = new Vector4(1, 0, 1, 1),
                               Id = Guid.NewGuid(),
                           },
                       new()
                           {
                               NormalizedPosition = 1,
                               Color = new Vector4(0, 0, 1, 1),
                               Id = Guid.NewGuid(),
                           },
                   };
    }

    public sealed class Step
    {
        public float NormalizedPosition;
        public Vector4 Color;
        public Guid Id;

        public Step()
        {
        }

        /// <summary>
        /// Constructor to clone the provided <see cref="Step"/>
        /// </summary>
        /// <param name="original"></param>
        internal Step(Step original)
        {
            NormalizedPosition = original.NormalizedPosition;
            Color = original.Color;
            Id = original.Id;
        }
    }

    public enum Interpolations
    {
        Linear,
        Hold,
        Smooth,
        OkLab,
        Spline,
    }

    private readonly CubicSpline[] _cubicSpline = new CubicSpline[4];

    private float[] _xValues;
    private float[] _yValues0;
    private float[] _yValues1;
    private float[] _yValues2;
    private float[] _yValues3;
    private int _lastHash;

    private void InitCubicSplines()
    {
        _xValues = new float [Steps.Count];
        _yValues0 = new float[Steps.Count];
        _yValues1 = new float[Steps.Count];
        _yValues2 = new float[Steps.Count];
        _yValues3 = new float[Steps.Count];

        for (var stepIndex = 0; stepIndex < Steps.Count; stepIndex++)
        {
            var step = Steps[stepIndex];
            _xValues[stepIndex] = step.NormalizedPosition;
            _yValues0[stepIndex] = step.Color.X;
            _yValues1[stepIndex] = step.Color.Y;
            _yValues2[stepIndex] = step.Color.Z;
            _yValues3[stepIndex] = step.Color.W;
        }

        _cubicSpline[0] = new CubicSpline(_xValues, _yValues0);
        _cubicSpline[1] = new CubicSpline(_xValues, _yValues1);
        _cubicSpline[2] = new CubicSpline(_xValues, _yValues2);
        _cubicSpline[3] = new CubicSpline(_xValues, _yValues3);
    }

    /// <summary>
    /// A lame attempt to avoid recomputation of spline.
    /// Probably premature optimizing but I didn't find an obvious method
    /// to flag a gradient as modified.
    /// </summary>
    /// <remarks>
    /// Sampling 30K points takes roughly 5ms. 
    /// </remarks>
    public override int GetHashCode()
    {
        var hash = 31.GetHashCode();
        foreach (var s in Steps)
        {
            hash = hash * 31 + s.NormalizedPosition.GetHashCode();
            hash = hash * 31 + s.Color.GetHashCode();
        }

        return hash;
    }

    private Vector4 SampleSpline(float t)
    {
        var hash = GetHashCode();
        if (hash != _lastHash)
        {
            _lastHash = hash;
            InitCubicSplines();
        }

        var uniArray = new[] { t };
        return new Vector4(
                           MathF.Max(_cubicSpline[0].Eval(uniArray)[0], 0),
                           MathF.Max(_cubicSpline[1].Eval(uniArray)[0], 0),
                           MathF.Max(_cubicSpline[2].Eval(uniArray)[0], 0),
                           _cubicSpline[3].Eval(uniArray)[0].Clamp(0, 1)
                          );
    }
}