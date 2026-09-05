namespace Lib.image.color;


[Guid("025b32e9-b570-45be-ae84-50424982aae3")]
internal sealed class ToneMapping : Instance<ToneMapping>
{
    [Output(Guid = "05c886f7-2c2c-4fe8-8b66-d6967dc43367")]
    public readonly Slot<Texture2D> Output = new();

        
    [Input(Guid = "72e51856-bc8f-4bcf-a6d8-6c7b4f8b0583")]
    public readonly InputSlot<Texture2D> Texture2d = new();

    [Input(Guid = "37d4e1e6-e5a0-40b6-ada9-9481f5a807de", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "be44974c-2685-40ff-86b6-bdf1cc38eee4")]
    public readonly InputSlot<bool> CorrectGamma = new();

    [Input(Guid = "dfd590f5-a930-42df-aecd-52f8d0195369")]
    public readonly InputSlot<float> Gamma = new InputSlot<float>();

        [Input(Guid = "b3ac35c2-7f59-49fb-a4ad-84dc5a8c3841")]
        public readonly InputSlot<float> Exposure = new InputSlot<float>();

    private enum Modes
    {
        Aces,
        Reinhard,
        Filmic,
        Uncharted2,
        AgX,
        AgX_Punchy,
        None,
    }
}