using T3.Core.Utils;

namespace Lib.numbers.color;

[Guid("11958088-6358-4df1-b80f-c45fa6889a2e")]
internal sealed class PickGradient : Instance<PickGradient>
{
    [Output(Guid = "64ae82b9-fc16-4747-b7ac-cd5983226f64")]
    public readonly Slot<Gradient> Selected = new();

    public PickGradient()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var connections = Gradients.GetCollectedTypedInputs();
        if (connections == null || connections.Count == 0)
            return;

        var index = Index.GetValue(context).Mod(connections.Count);
        Selected.Value = connections[index].GetValue(context);

        // Clear dirty flag
        if (_isFirstUpdate)
        {
            foreach (var c in connections)
            {
                c.GetValue(context);
            }

            _isFirstUpdate = false;
        }

        Gradients.DirtyFlag.Clear();
    }

    private bool _isFirstUpdate = true;

    [Input(Guid = "F68298DC-F8D0-4D08-AFF5-7D35200F18B3")]
    public readonly MultiInputSlot<Gradient> Gradients = new();

    [Input(Guid = "B8326DD4-9224-4D37-A490-7686184D8658")]
    public readonly InputSlot<int> Index = new();
}