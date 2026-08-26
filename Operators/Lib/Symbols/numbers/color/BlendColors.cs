using T3.Core.Utils;

namespace Lib.numbers.color;

[Guid("6b7c541a-ca36-4f21-ac95-89e874820c5a")]
internal sealed class BlendColors : Instance<BlendColors>
{
    [Output(Guid = "66ce8660-253c-4a0b-8aec-f7a56751a1e4")]
    public readonly Slot<Vector4> Color = new();

    public BlendColors()
    {
        Color.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var c1 = ColorA.GetValue(context);
        var c2 = ColorB.GetValue(context);
        var m = Factor.GetValue(context);
        var mode = (Modes)Mode.GetValue(context);

        Vector4 result; 
        switch (mode)
        {
            case Modes.Mix:
                //result = Color.Value;
                result = c1 * (1-m) + c2 * m;
                break;
            case Modes.Multiply:
                    
                var factor = MathUtils.Lerp(new Vector4(1), c2, m);
                result = c1 * factor ;
                    
                break;
                
            case Modes.Add:
                result = c1 +  c2 * m;
                break;

            case Modes.Blend:
            {
                result =  (1f - c2.W) * c1 + c2.W * c2;
                result.W = c1.W + c2.W - c1.W * c2.W;
                    
                //float a = tA.a + tB.a - tA.a*tB.a;                        
                //result = c1 +  c2 * m;
                break;
            }


            default:
                throw new ArgumentOutOfRangeException();
                
        }

        Color.Value = result; //Vector4.Max(result, Vector4.Zero);
    }
        
    [Input(Guid = "EB601C57-2025-4135-8292-223EAEDAF187")]
    public readonly InputSlot<Vector4> ColorA = new();
        
    [Input(Guid = "B9E5C6F3-7052-456F-9D1B-C182B4412433")]
    public readonly InputSlot<Vector4> ColorB = new();
        
    [Input(Guid = "40803D0E-C37C-4B5D-B64B-FD1EF090A4F7")]
    public readonly InputSlot<float> Factor = new();

    [Input(Guid = "8D444E8C-D9B5-4206-9202-5ABF23B13BAF", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    private enum Modes
    {
        Mix,
        Multiply,
        Add,
        Blend,
    }
}