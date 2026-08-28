using T3.Core.Utils;

namespace Lib.flow.context;

[Guid("8a104da8-3995-421f-b756-b6fc06953be3")]
internal sealed class SetRequestedResolutionCmd : Instance<SetRequestedResolutionCmd>
{
    public SetRequestedResolutionCmd()
    {
        Result.UpdateAction += Update;
    }    
        
    [Output(Guid = "6E452022-EDE6-455A-A23B-5CB00ED1E1F1")]
    public readonly Slot<Command> Result = new();
        
    private void Update(EvaluationContext context)
    {
        var previousResolution = context.RequestedResolution;
        var resolutionFactor = ScaleResolution.GetValue(context);
            
        var requestedResolution = Resolution.GetValue(context);
        var resolutionStretch = StretchResolution.GetValue(context);

        var resolutionUpdate = requestedResolution.X > 0 && requestedResolution.Y > 0 && resolutionStretch.X > 0 && resolutionStretch.Y > 0;
        var newResolution = resolutionUpdate ? requestedResolution : previousResolution;
            
        context.RequestedResolution = new Int2((int)(newResolution.X * resolutionFactor * resolutionStretch.X).Clamp(1, 16384),
                                               (int)(newResolution.Y * resolutionFactor * resolutionStretch.Y).Clamp(1, 16384));
            
        Result.Value = Texture.GetValue(context);
        context.RequestedResolution = previousResolution;
    }

    [Input(Guid = "C59596CA-984F-4682-88D3-808C60665414")]
    public readonly InputSlot<Command> Texture = new();
        
    [Input(Guid = "58b2a52c-ece7-420b-9730-e3d0406a5616")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "B05D30D3-3BC4-4517-BD3D-ED2D26F87646")]
    public readonly InputSlot<Vector2> StretchResolution = new();
    
    [Input(Guid = "a4d09d43-5f81-4b15-85db-e03f917b9764")]
    public readonly InputSlot<float> ScaleResolution = new();


}