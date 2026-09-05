using T3.Core.Utils;

namespace Lib.numbers.color;

[Guid("b9999f07-da19-45b9-ae12-f9d0662c694c")]
internal sealed class BlendGradients : Instance<BlendGradients>
{
    [Output(Guid = "D457933E-6642-471E-807A-6C22008BBD0C")]
    public readonly Slot<Gradient> Result = new();

    public BlendGradients()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var blendMode = (BlendModes)BlendMode.GetValue(context);
        var gradientA = GradientA.GetValue(context);
        var gradientB = GradientB.GetValue(context);
        var mixFactor = MixFactor.GetValue(context).Clamp(0,1);
            
        _steps.Clear();
        foreach (var stepA in gradientA.Steps)
        {
            var positionA = stepA.NormalizedPosition;
            var colorA = stepA.Color;
            var colorB = gradientB.Sample(positionA);
            var blendedColor = BlendColors(colorA, colorB, blendMode, mixFactor);
            _steps[positionA] = blendedColor;
        }

        foreach (var stepB in gradientB.Steps)
        {
            var positionB = stepB.NormalizedPosition;
            var colorB = stepB.Color;
            var colorA = gradientA.Sample(positionB);
            var blendedColor = BlendColors(colorA, colorB, blendMode, mixFactor);
            _steps[positionB] = blendedColor;
        }

        var positions = _steps.Keys.ToArray();
        var steps = new List<Gradient.Step>(positions.Length);
        Array.Sort(positions);

        foreach (var p in positions)
        {
            steps.Add( new Gradient.Step()
                           {
                               Color = _steps[p],
                               Id = new Guid(),
                               NormalizedPosition = p,
                           });
        }

        var result = new Gradient()
                         {
                             Steps =  steps,
                             Interpolation = Gradient.Interpolations.Linear,
                         };
            
        Result.Value = result;
    }

    private Vector4 BlendColors(Vector4 a, Vector4 b, BlendModes blendMode, float mixFactor)
    {
        switch (blendMode)
        {
            case BlendModes.Normal:
            {
                var alpha = a.W + b.W - a.W*b.W;    
                return new Vector4(
                                   (1.0f - b.W)*a.X + b.W*b.X,
                                   (1.0f - b.W)*a.Y + b.W*b.Y,
                                   (1.0f - b.W)*a.Z + b.W*b.Z,
                                   alpha
                                  );
            }
                
            case BlendModes.Multiply:
            {
                var r = a * b;
                r.W = a.W + b.W - a.W*b.W;
                return r;
            }

            case BlendModes.Screen:
            {
                var r=  Vector4.One-( Vector4.One-a) * (Vector4.One-b);
                r.W = a.W + b.W - a.W*b.W;
                return r;
            }

            case BlendModes.Mix:
            {
                return Vector4.Lerp(a, b, mixFactor);
            }
        }
            
        return Vector4.One;
    }
        
    private Dictionary<float, Vector4> _steps = new(20);

    private enum BlendModes
    {
        Normal,
        Multiply,
        Screen,
        Mix,
    }
        
        
    [Input(Guid = "AFA38628-B616-4B06-878D-EC554050F2B0")]
    public readonly InputSlot<Gradient> GradientA = new();
        
    [Input(Guid = "C1856EF1-BCBE-4377-A910-E8EEF7D963DA")]
    public readonly InputSlot<Gradient> GradientB = new();
        
    [Input(Guid = "EDABC753-2CCA-4F8D-8F14-2B25479C2188", MappedType = typeof(BlendModes))]
    public readonly InputSlot<int> BlendMode = new();
        
    [Input(Guid = "C21371D6-1735-43D4-96FF-D04CBCE0FEC9", MappedType = typeof(BlendModes))]
    public readonly InputSlot<float> MixFactor = new();        
}