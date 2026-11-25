using Lib.Utils;

namespace Lib.render._dx11.buffer;

[Guid("e6bbbeef-08d8-4105-b84d-39edadb549c0")]
internal sealed class PickBuffer : Instance<PickBuffer>
{
    [Output(Guid = "32D2645B-B627-437A-AFEC-7E728E2B54F5")]
    public readonly Slot<BufferWithViews> Output = new();
        
    [Output(Guid = "106C3BD6-BC99-4B7E-A411-E3044476D8E7")]
    public readonly Slot<int> Count = new();
        
        
    public PickBuffer()
    {
        Output.UpdateAction += Update;
        Count.UpdateAction += Update;
    }
    
    private void Update(EvaluationContext context)
    {
        Count.Value = Input.GetCollectedTypedInputs().Count;
        if (PickedInput.TryGetValue(Input, Index.GetValue(context), context, out var value))
            Output.Value = value;

        Output.DirtyFlag.Clear();
        Count.DirtyFlag.Clear();
    }

    [Input(Guid = "6B1C6232-819A-4021-82A9-994F8928BE13")]
    public readonly MultiInputSlot<BufferWithViews> Input = new();

    [Input(Guid = "04776dc8-7b84-41f5-973c-22cadbf44f02")]
    public readonly InputSlot<int> Index = new();

}