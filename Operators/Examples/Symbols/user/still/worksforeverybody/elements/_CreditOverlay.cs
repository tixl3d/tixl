namespace Examples.user.still.worksforeverybody.elements;

[Guid("cea732a4-c4c2-40df-a0ff-f209125e7c99")]
internal sealed class _CreditOverlay : Instance<_CreditOverlay>
{
    [Output(Guid = "4404e0a7-2470-4c14-8ebc-b61a641c9a6c")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "30825b6b-8fbd-4cbf-a56c-ec62a4e8948d")]
    public readonly InputSlot<string> Titles = new();

    [Input(Guid = "beb1768f-594a-4bef-b516-093161c5f1cc")]
    public readonly InputSlot<int> Index = new();

    [Input(Guid = "40420d46-f866-411e-a5fa-6d3996c6cf10")]
    public readonly InputSlot<float> Visibility = new();

    [Input(Guid = "69707763-2952-493d-8ccd-c41bf9b86ca0", MappedType = typeof(Styles))]
    public readonly InputSlot<int> Style = new();

    [Input(Guid = "68151ebb-2476-4f30-a41d-1425fe48a4e5")]
    public readonly InputSlot<bool> ShowDebug = new();

    [Input(Guid = "eae23f5a-68ef-464e-a7e5-a465b01370de")]
    public readonly InputSlot<System.Numerics.Vector3> Translation = new();


    private enum Styles
    {
        WhiteOnBlack,
        BlackOnWhite,
    }
}