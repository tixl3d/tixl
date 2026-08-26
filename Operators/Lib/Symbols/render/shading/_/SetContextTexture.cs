using T3.Core.Rendering.Material;

namespace Lib.render.shading.@_;

[Guid("944d1903-cd23-49ca-9b0d-2fc73bfcfd30")]
internal sealed class SetContextTexture : Instance<SetContextTexture>
{
    [Output(Guid = "db61864d-0dd4-44bf-9722-0b9ce7e8fdd4")]
    public readonly Slot<Command> Output = new();

    public SetContextTexture()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var id = Id.GetValue(context);
            
        //var previousMap = context.PrbPrefilteredSpecular;
        var tex = Texture.GetValue(context) ?? PbrContextSettings.WhitePixelTexture;
        var hadPreviousTexture = context.ContextTextures.TryGetValue(id, out var previousTexture);
        context.ContextTextures[id] = tex;
            
        SubTree.GetValue(context);
        if (hadPreviousTexture)
        {
            context.ContextTextures[id] = previousTexture;
        }
        else
        {
            context.ContextTextures.Remove(id);
        }
        
    }

    [Input(Guid = "16863415-1d90-46a7-80a4-372602a49c6f")]
    public readonly InputSlot<Command> SubTree = new();

    [Input(Guid = "1CD51956-0E0C-4B3F-B071-8D86CDCB7080")]
    public readonly InputSlot<string> Id = new();
        
    [Input(Guid = "3ab2e94d-b10b-4cd9-9ee0-073292a947fc")]
    public readonly InputSlot<Texture2D> Texture = new();

}