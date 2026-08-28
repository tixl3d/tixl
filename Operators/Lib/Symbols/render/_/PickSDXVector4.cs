using T3.Core.Utils;

namespace Lib.render.@_;

[Guid("a83f2e4f-cb4d-4a6f-9f7a-2ea7fdfab54b")]
internal sealed class PickSDXVector4 : Instance<PickSDXVector4>
{
    [Output(Guid = "B0A0DD4C-90BB-49E9-BA83-E96C3FAB2929")]
    public readonly Slot<float> Value1 = new();

    [Output(Guid = "C46BCD47-C620-4894-8E0D-9202C1790914")]
    public readonly Slot<float> Value2 = new();

    [Output(Guid = "3349F39A-7980-4EFD-849C-70A4C13D5177")]
    public readonly Slot<float> Value3 = new();

    [Output(Guid = "C5EA9711-6326-4EDC-932B-35FD11323E4F")]
    public readonly Slot<float> Value4 = new();

        
    public PickSDXVector4()
    {
        Value1.UpdateAction += Update;
        Value2.UpdateAction += Update;
        Value3.UpdateAction += Update;
        Value4.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var list = Input.GetValue(context);
        if (list == null || list.Length == 0)
        { 
            return;
        }

        var index = Index.GetValue(context);

        var v= list[index.Mod(list.Length)];

        Value1.Value = v.X;
        Value2.Value = v.Y;
        Value3.Value = v.Z;
        Value4.Value = v.W;

        Value1.DirtyFlag.Clear();
        Value2.DirtyFlag.Clear();
        Value3.DirtyFlag.Clear();
        Value4.DirtyFlag.Clear();
    }

    [Input(Guid = "0f9eebb0-6f13-4751-abac-15a467ad56c2")]
    public readonly InputSlot<Vector4[]> Input = new();

    [Input(Guid = "dbc92e88-cae2-44a8-b291-1a6168624244")]
    public readonly InputSlot<int> Index = new(0);
}