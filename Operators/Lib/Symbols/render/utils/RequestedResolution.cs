using Lib.Utils;

namespace Lib.render.utils;

[Guid("c0a26813-bc97-4c42-b051-53a9a5913331")]
internal sealed class RequestedResolution : Instance<RequestedResolution>
{
    [Output(Guid = "dd1c6ce4-fb30-47b6-8325-5f645279ef2d", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Int2> Size = new();

    [Output(Guid = "FE01CC08-0573-4CD3-970C-67FC2B0A4E60", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Width = new();

    [Output(Guid = "8E34259F-C017-474A-AAC6-D21ACCBAD23E", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<int> Height = new();

    [Output(Guid = "0F5CB8A7-8CC5-4A2D-9741-C8A3B9ADC049", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> AspectRatio = new();

    public RequestedResolution()
    {
        Size.UpdateAction += Update;
        Width.UpdateAction += Update;
        Height.UpdateAction += Update;
        AspectRatio.UpdateAction += Update;
        _resolutionTracker = new RequestedResolutionTracker(Size, Width, Height, AspectRatio);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            _resolutionTracker.Dispose();
    }

    private void Update(EvaluationContext context)
    {
        var resolution = context.RequestedResolution;
        Size.Value = resolution;
        Width.Value = resolution.Width;
        Height.Value = resolution.Height;
        AspectRatio.Value = resolution.Width / (float)resolution.Height;
        _resolutionTracker.Track(resolution);
    }

    private readonly RequestedResolutionTracker _resolutionTracker;
}
