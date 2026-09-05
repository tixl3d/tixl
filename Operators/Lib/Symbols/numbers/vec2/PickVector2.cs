using T3.Core.Utils;

namespace Lib.numbers.vec2;

[Guid("1cd2f5b1-bc54-4405-8ee6-d1acf59ebe14")]
internal sealed class PickVector2 : Instance<PickVector2>
{
    [Output(Guid = "8c28230f-0ff8-4158-b6db-33ffe7b64ad5")]
    public readonly Slot<Vector2> Selected = new();

    public PickVector2()
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

    [Input(Guid = "f210558a-4f9e-4e94-bbd4-ef73da498614")]
    public readonly MultiInputSlot<Vector2> Input = new();

    [Input(Guid = "dad7d212-e895-44d9-83cc-69fb11898aec")]
    public readonly InputSlot<int> Index = new(0);
}