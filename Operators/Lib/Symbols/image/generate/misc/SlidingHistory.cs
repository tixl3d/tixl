namespace Lib.image.generate.misc;

[Guid("1b69d98a-0b38-4563-aa43-aac5b8395c2b")]
internal sealed class SlidingHistory : Instance<SlidingHistory>
{
    [Output(Guid = "724ecde4-fd33-4a59-a8df-51cb21e70bd3")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "48699e66-a59b-4cbb-b131-171ce9fcade3")]
    public readonly InputSlot<Texture2D> Texture2d = new();

    [Input(Guid = "41748add-f957-4a48-b7a5-43ff868bc814")]
    public readonly InputSlot<int> HistoryLength = new();

    [Input(Guid = "5d42ff17-a552-4437-877a-a27a369866d7")]
    public readonly InputSlot<bool> IsEnabled = new();

    [Input(Guid = "72db9342-9c07-4557-8b67-e3dc6ee55271")]
    public readonly InputSlot<bool> ResetTrigger = new();

    [Input(Guid = "adc812a8-d86b-4ac9-b33f-99f78e0c8c44", MappedType = typeof(Directions))]
    public readonly InputSlot<int> Direction = new();

    [Input(Guid = "561b8bdc-557c-4a7d-8759-14486def65e4")]
    public readonly InputSlot<float> SourceSlice = new();

    private enum Directions
    {
        Down,
        Left,
        Right,
    }
}