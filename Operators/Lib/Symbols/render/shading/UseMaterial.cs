using T3.Core.Rendering.Material;

namespace Lib.render.shading;

[Guid("11c5b420-d20b-42df-bc96-8a9e851521e5")]
internal sealed class UseMaterial : Instance<UseMaterial>
{
    [Output(Guid = "82d08a85-69ce-4fe9-a1cc-cc211456719e")]
    public readonly Slot<Command> Output = new();

    public UseMaterial()
    {
        Output.UpdateAction += Update;
    }
        
    private void Update(EvaluationContext context)
    {
        var pbrMaterial = MaterialReference.GetValue(context);
        var isValid = pbrMaterial != null;
            
        var previousMaterial = context.PbrMaterial;
        if (isValid)
        {
            context.Materials.Add(pbrMaterial);
            context.PbrMaterial = pbrMaterial;
        }
            
        SubTree.GetValue(context);

        if (isValid)
        {
            context.Materials.RemoveAt(context.Materials.Count - 1);
        }
            
        context.PbrMaterial = previousMaterial;
    }

        
    [Input(Guid = "61613a9e-a67c-4a68-9668-c986895f0945")]
    public readonly InputSlot<Command> SubTree = new();

    [Input(Guid = "E3A3BC6C-149B-47AE-A73C-A2C5EE571253")]
    public readonly InputSlot<PbrMaterial> MaterialReference = new();
}