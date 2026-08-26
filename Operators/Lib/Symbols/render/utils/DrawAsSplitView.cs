namespace Lib.render.utils;

[Guid("f19a9234-cd23-4229-a794-aa9d97ad8027")]
internal sealed class DrawAsSplitView : Instance<DrawAsSplitView>
{
    [Output(Guid = "65456554-355b-41a3-893e-960d28113f53")]
    public readonly Slot<Command> Output = new();
        
    [Input(Guid = "a3929303-170b-496a-b8e0-fc5f604a0ec7")]
    public readonly MultiInputSlot<Command> Commands = new();

    [Input(Guid = "987bda72-6a6b-4216-9ecf-d87b7299553d")]
    public readonly InputSlot<string> Labels = new();

    [Input(Guid = "3cb0dfab-deaa-4ed4-ba45-ac63e886e212")]
    public readonly InputSlot<Vector2> Stretch = new();

    [Input(Guid = "b1cdb551-0045-42d4-a6ba-fa8aa0f1f98f", MappedType = typeof(ViewModes))]
    public readonly InputSlot<int> Mode = new();
        
        
    private enum ViewModes
    {
        RepeatView,
        SliceView,
    }
}