using T3.Core.Utils;

namespace Lib.numbers.vec4;

[Guid("3539c3a0-73f1-412c-aad2-517810126ac6")]
internal sealed class PickColor : Instance<PickColor>
{
    [Output(Guid = "E97325E4-7A64-4F38-B7B1-92EDD73BFB77")]
    public readonly Slot<Vector4> Selected = new();

    public PickColor()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var connections = Input.GetCollectedTypedInputs();
        if (connections == null || connections.Count == 0)
            return;

        var index = Index.GetValue(context).Mod(connections.Count);
        Selected.Value = connections[index].GetValue(context);
    }

    [Input(Guid = "03A48F06-5375-449F-941B-35F93B2A5825")]
    public readonly MultiInputSlot<Vector4> Input = new();

    [Input(Guid = "F3F93FEB-8B26-4F6D-BD90-4C331CF9FAC0")]
    public readonly InputSlot<int> Index = new(0);
}