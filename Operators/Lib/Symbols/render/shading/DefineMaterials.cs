using T3.Core.Rendering.Material;

namespace Lib.render.shading;

[Guid("0bd77dd6-a93a-4e2e-b69b-bbeb73cb5ae9")]
internal sealed class DefineMaterials : Instance<DefineMaterials>
{
    [Output(Guid = "9e5f90f2-370c-4aef-8819-c3c5c7e5edc2")]
    public readonly Slot<Command> Output = new();

    public DefineMaterials()
    {
        Output.UpdateAction += Update;
    }
        

    private Dictionary<string, PbrMaterial> _materials = new();
        
    private void Update(EvaluationContext context)
    {
        var materialsInputs = Materials.GetCollectedTypedInputs();

        var addedMaterialsCount = 0;
        var previousMaterialCount = context.Materials.Count;
            
        foreach (var slot in materialsInputs)
        {
            var m = slot.GetValue(context);

            if (m == null)
                continue;

            addedMaterialsCount++;
            context.Materials.Add(m);
        }
            
            
        // Update subgraph
        SubGraph.GetValue(context);

        if(addedMaterialsCount > 0)
            context.Materials.RemoveRange( previousMaterialCount, addedMaterialsCount );
            
    }


    [Input(Guid = "ab03556d-02a5-4329-94af-58563f93e159")]
    public readonly InputSlot<Command> SubGraph = new();
        
        
    [Input(Guid = "6299A306-D96C-425D-B44C-0340358439CB")]
    public readonly MultiInputSlot<PbrMaterial> Materials = new();

        
}