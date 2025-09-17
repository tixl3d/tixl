namespace Examples.user.pixtur.research;

[Guid("f15cc064-1d70-4945-ae60-35d884788c0f")]
public class _SubdivsionRaster : Instance<_SubdivsionRaster>
{

    [Output(Guid = "FBDF0455-001D-4226-B89F-9D2DBFFCA515")]
    public readonly Slot<Texture2D> OutBuffer = new();
        
        
    [Input(Guid = "971d44e4-fc34-4dc2-9e94-e6cc202b1ef6")]
    public readonly InputSlot<Int2> Int2 = new();

    [Input(Guid = "e618891e-2cdb-4464-8f72-577a11b0bb14")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "70a7d763-a8b3-45e7-8f7a-24ec27a31ba6")]
    public readonly InputSlot<Texture2D> Texture = new();

    [Input(Guid = "3332b1e1-282d-4c41-9fe3-e609611ade51")]
    public readonly InputSlot<float> LineWidth = new();

}