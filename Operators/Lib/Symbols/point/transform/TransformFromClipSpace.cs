namespace Lib.point.transform;

[Guid("81377edc-0a42-4bb1-9440-2f2433d5757f")]
internal sealed class TransformFromClipSpace : Instance<TransformFromClipSpace>
{
    [Output(Guid = "fa70200b-cfcb-4efe-afbd-48cefea1ca39")]
    public readonly Slot<BufferWithViews> Output = new();

    [Input(Guid = "e02d3e37-4da6-4528-b06f-6f26c818d1d8")]
    public readonly InputSlot<BufferWithViews> Points = new();
        
        
        
    private enum Spaces
    {
        PointSpace,
        ObjectSpace,
    }
        
        
        
        
        
}