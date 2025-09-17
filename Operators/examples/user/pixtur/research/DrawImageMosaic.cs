namespace Examples.user.pixtur.research;

[Guid("816eda66-79c3-430c-9f03-d51b93b17987")]
public class DrawImageMosaic : Instance<DrawImageMosaic>
{
    [Output(Guid = "7141e084-d5c6-4bbc-b30c-20f3826bdfd1")]
    public readonly Slot<Command> Render = new();

    [Input(Guid = "2c0655fe-60d6-4a75-a5d5-881f35b148c9")]
    public readonly InputSlot<bool> TriggerUpdate = new();

    [Input(Guid = "271eb784-d6e4-4a24-a078-c5ecfd2ef19c")]
    public readonly InputSlot<string> ImageFolder = new();

    [Input(Guid = "0a8188a1-6a5f-44bf-92dc-ada83c1df1da")]
    public readonly InputSlot<float> Size = new();

    [Input(Guid = "9e12ec02-55a1-4461-8f19-4766a45c08ea")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> PointBuffer = new();

    [Input(Guid = "9f53c882-49e5-4ff4-9862-f33a8b8aedd7")]
    public readonly InputSlot<Texture2D> ReferenceImage = new();

}