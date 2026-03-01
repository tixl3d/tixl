using T3.Core.Utils;

namespace Lib.numbers.@int.logic;

[Guid("81555155-ae6f-40aa-961d-b6badb77af21")]
internal sealed class PickInt : Instance<PickInt>
{
    [Output(Guid = "9DDD1C52-865A-4930-84EC-98D3C0FFAA9C")]
    public readonly Slot<int> Selected = new();

    public PickInt()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var connections = InputValues.GetCollectedTypedInputs();
        var index = Index.GetValue(context).Mod(connections.Count);
        InputValues.DirtyFlag.Clear();
        
        if (connections.Count == 0)
            return;

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
    }
    
    private bool _isFirstUpdate = true;

    [Input(Guid = "2C0A4EB2-DA56-449D-91B8-5BA0870FBEB4")]
    public readonly MultiInputSlot<int> InputValues = new();

    [Input(Guid = "8bbc412b-f574-4a2b-9cbc-bf4f60aebb17")]
    public readonly InputSlot<int> Index = new(0);
}