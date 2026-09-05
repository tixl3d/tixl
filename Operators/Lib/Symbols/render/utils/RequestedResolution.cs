namespace Lib.render.utils;

[Guid("c0a26813-bc97-4c42-b051-53a9a5913331")]
internal sealed class RequestedResolution : Instance<RequestedResolution>
{
    [Output(Guid = "dd1c6ce4-fb30-47b6-8325-5f645279ef2d", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Int2> Size = new();

    [Output(Guid = "FE01CC08-0573-4CD3-970C-67FC2B0A4E60", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<int> Width = new();

    [Output(Guid = "8E34259F-C017-474A-AAC6-D21ACCBAD23E", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<int> Height = new();

    [Output(Guid = "0F5CB8A7-8CC5-4A2D-9741-C8A3B9ADC049", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<float> AspectRatio = new();

        
    public RequestedResolution()
    {
        Size.UpdateAction += Update;
        Width.UpdateAction += Update;
        Height.UpdateAction += Update;
        AspectRatio.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        Size.Value = context.RequestedResolution;
            
        var width = context.RequestedResolution.Width;
        Width.Value = width;
            
        var height = context.RequestedResolution.Height;
        Height.Value = height;

        AspectRatio.Value = width / (float)height;
    }
}