using T3.Core.Utils;

namespace Lib.render.shading;

[Guid("5b3a0c1d-87d6-4d88-ae20-ff9f95049ecf")]
internal sealed class SetRequestedResolution : Instance<SetRequestedResolution>
{
    public SetRequestedResolution()
    {
        Result.UpdateAction += Update;
    }    
        
    [Output(Guid = "2ABE986A-6A21-4CEB-B907-5B6E317E34A1")]
    public readonly Slot<Texture2D> Result = new();
        
    private void Update(EvaluationContext context)
    {
        var previousResolution = context.RequestedResolution;
        var resolutionFactor = ScaleResolution.GetValue(context);
            
        var requestedResolution = Resolution.GetValue(context);
        var newResolution = requestedResolution.X > 0 && requestedResolution.Y > 0 ? requestedResolution : previousResolution;
            
        context.RequestedResolution = new Int2((int)(newResolution.X * resolutionFactor).Clamp(1, 16384),
                                               (int)(newResolution.Y * resolutionFactor).Clamp(1, 16384));
            
        Result.Value = Texture.GetValue(context);
        context.RequestedResolution = previousResolution;
    }

    [Input(Guid = "DD52DD4F-84EE-421B-91E9-DA0B90ADB1A6")]
    public readonly InputSlot<Texture2D> Texture = new();
        
    [Input(Guid = "D6303C59-5EA2-43B3-8E73-218B99D7DF5F")]
    public readonly InputSlot<Int2> Resolution = new();

    [Input(Guid = "C545B200-B3A8-42A8-AC83-D85C3B6E58EE")]
    public readonly InputSlot<float> ScaleResolution = new();


}