namespace Examples.user.pixtur.research;

[Guid("0bc39951-6eec-493c-b609-9e07a9a1dcee")]
public class GenArMarker : Instance<GenArMarker>
{
    [Output(Guid = "457c5d64-9937-46f1-bfde-df3db22240a5")]
    public readonly Slot<Texture2D> ColorBuffer = new();


    [Input(Guid = "c4ee2560-49c8-403d-9d91-331017782544")]
    public readonly InputSlot<int> Seed = new();

    [Input(Guid = "09d5357f-5717-4a83-b3ad-ef134e5fda28")]
    public readonly InputSlot<int> RingCount = new();

    [Input(Guid = "c24a9acd-8f5d-4570-a386-85a0d692e5bb")]
    public readonly InputSlot<float> SegmentCount = new();

    [Input(Guid = "699c6fc7-bbcc-4943-8cfd-495b5f8e3120")]
    public readonly InputSlot<float> SegmentVariation = new();

    [Input(Guid = "16521e7a-7f03-47c5-a10d-e719a367953d")]
    public readonly InputSlot<float> Thickness = new();

    [Input(Guid = "3461f4dc-90a6-444f-841b-1d8f4aaf122e")]
    public readonly InputSlot<float> ThicknessVariation = new();

}