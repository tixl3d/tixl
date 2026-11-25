using Lib.Utils;

namespace Lib.mesh.modify;

[Guid("845371ef-a5c2-4ca2-8315-ea2b62f63ee2")]
internal sealed class PickMeshBuffer : Instance<PickMeshBuffer>
{
    [Output(Guid = "2F4733F8-ADF1-4A6D-B207-5EE2D566CAE3")]
    public readonly Slot<MeshBuffers> Output = new();
        
        
    public PickMeshBuffer()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (PickedInput.TryGetValue(Input, Index.GetValue(context), context, out var value))
            Output.Value = value;
    }

    [Input(Guid = "7BB6F999-214A-448A-A7F7-BE447113785E")]
    public readonly MultiInputSlot<MeshBuffers> Input = new MultiInputSlot<MeshBuffers>();

    [Input(Guid = "076AFDCC-C9AF-4875-B97A-D8132996B35A")]
    public readonly InputSlot<int> Index = new InputSlot<int>();
}