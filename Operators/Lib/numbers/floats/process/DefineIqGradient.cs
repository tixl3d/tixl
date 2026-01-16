#nullable enable
using SharpDX;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.numbers.floats.process;

[Guid("44cee07e-2a71-4dd5-9ded-8274e8e19332")]
internal sealed class DefineIqGradient : Instance<DefineIqGradient>
{
    [Output(Guid = "0DBDE53C-92D3-4A21-80F0-62DF913460CF")]
    public readonly Slot<Gradient> Gradient = new();

    public DefineIqGradient()
    {
        Gradient.UpdateAction += Update;
        Gradient.Value = new Gradient();
    }

    private void Update(EvaluationContext context)
    {
        var gradient = Gradient.Value;
        var a = A_Brightness.GetValue(context);
        var b = B_Contrast.GetValue(context);
        var c = C_Frequency.GetValue(context);
        var d = D_Phase.GetValue(context);

        var phase = Phase.GetValue(context);

        var count = NumOfSteps.GetValue(context).Clamp(1, 256);
        if (count != gradient.Steps.Count)
        {
            gradient.Steps.Clear();
            for (var i = 0; i < count; i++)
            {
                var pos = count > 1 ? (float)i / (count - 1) : 0;  
                gradient.Steps.Add(new Gradient.Step
                                        {
                                            NormalizedPosition = pos,
                                            Color = Vector4.One,
                                            Id = Guid.NewGuid()
                                        });
            }
        }

        for (var i = 0; i < count; i++)
        {
            var f = count > 1 ? (float)i / (count - 1) : 0;
            f += phase;

            var color = new Vector4((a.X + b.X * MathF.Cos(MathF.Tau *(f * c.X  + d.X))).Clamp(0,1000),
                                      (a.Y + b.Y * MathF.Cos(MathF.Tau *(f * c.Y  + d.Y))).Clamp(0,1000),
                                      (a.Z + b.Z * MathF.Cos(MathF.Tau *(f * c.Z  + d.Z))).Clamp(0,1000),
                                      (a.W + b.W * MathF.Cos(MathF.Tau *(f * c.W  + d.W))).Clamp(0,1)
                                     );
            
            
            gradient.Steps[i].Color = color;
        }
        
        gradient.Interpolation = (Gradient.Interpolations)Interpolation.GetValue(context);
    }
    
    
    
    [Input(Guid = "750E53F0-5DDB-4BE1-BEF0-DA2E48231553")]
    public readonly InputSlot<Vector4> A_Brightness = new();
    
    [Input(Guid = "133F0D11-9DE9-49F6-9C76-BBAF6BED82F4")]
    public readonly InputSlot<Vector4> B_Contrast = new();

    [Input(Guid = "12D9B558-C83C-4D7B-B366-481D09E54501")]
    public readonly InputSlot<Vector4> C_Frequency = new();

    [Input(Guid = "887C0B40-C71F-4E0E-BFF2-0FDFF64F8C82")]
    public readonly InputSlot<Vector4> D_Phase = new();

    [Input(Guid = "85F98834-7921-46E6-9F63-9DA18B2A7BF8")]
    public readonly InputSlot<float> Phase = new();
    
    [Input(Guid = "96961db2-d597-4658-b0c0-fa1301a2fcdc")]
    public readonly InputSlot<int> NumOfSteps = new();
    
    [Input(Guid = "918c99f2-2197-467d-a991-2f69ef81440c", MappedType = typeof(Gradient.Interpolations))]
    public readonly InputSlot<int> Interpolation = new();
}