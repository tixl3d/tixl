namespace Lib.render.shading.@_;

[Guid("dee8f2de-5cbd-4ca7-9449-d6c74197546e")]
internal sealed class GetTextureFromContext : Instance<GetTextureFromContext>
{
    [Output(Guid = "C7CAC361-00D9-48D4-BE48-311551F3D449", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Texture2D> Output = new();

    public GetTextureFromContext()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var id = Id.GetValue(context);
            
        context.ContextTextures.TryGetValue(id, out var texture);
        Output.Value = texture;
    }
        
    [Input(Guid = "2986cf49-0ca3-46ce-8725-34f3c9d3c116")]
    public readonly InputSlot<string> Id = new();
        
}