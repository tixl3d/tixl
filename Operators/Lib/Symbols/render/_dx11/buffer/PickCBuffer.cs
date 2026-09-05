using SharpDX.D3DCompiler;
using T3.Core.Utils;

namespace Lib.render._dx11.buffer;

[Guid("690a64f6-7475-4766-a3f6-456fc5fcea86")]
internal sealed class PickCBuffer :Instance<PickCBuffer>{
    [Output(Guid = "397F3890-43BA-4C2D-8143-3424808AF5E3")]
    public readonly Slot<Buffer> Output = new();
        
    [Output(Guid = "bc197bba-0f85-4872-b88d-753cd151a9ad")]
    public readonly Slot<int> Count = new();
        
        
    public PickCBuffer()
    {
        Output.UpdateAction += Update;
        Count.UpdateAction += Update;
    }
    
    private void Update(EvaluationContext context)
    {
        var connections = Input.GetCollectedTypedInputs();
        var index = Index.GetValue(context);
        
        if (connections.Count == 0)
        {
            Count.Value = 0;
            Output.DirtyFlag.Clear();
            return;
        }

        Count.Value = connections.Count;
        Output.Value = connections[index.Mod(connections.Count)].GetValue(context);
            
        Output.DirtyFlag.Clear();
        Count.DirtyFlag.Clear();
    }

    [Input(Guid = "2168FD82-DF85-405E-820F-7E6447298891")]
    public readonly MultiInputSlot<Buffer> Input = new();

    [Input(Guid = "a4305e0f-3ceb-4353-acf3-41655dcffd93")]
    public readonly InputSlot<int> Index = new();

}