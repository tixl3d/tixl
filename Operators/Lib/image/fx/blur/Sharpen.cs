namespace Lib.image.fx.blur;

[Guid("4b207e35-64b4-4833-977c-da6c7154a081")]
internal sealed class Sharpen : Instance<Sharpen>
{
    [Output(Guid = "d412319c-42be-480d-a4e5-60b5b5b1722d")]
    public readonly Slot<Texture2D> TextureOutput = new();


    [Input(Guid = "cdc10025-36a4-4fae-ad59-110ea9343cb0")]
    public readonly InputSlot<Texture2D> Image = new();

    [Input(Guid = "d6c4daf8-caa3-4991-8d03-50eaad142b39")]
    public readonly InputSlot<float> SampleRadius = new();

    [Input(Guid = "def5bcf3-d499-41ad-82b8-1b9706ebaab6")]
    public readonly InputSlot<float> Strength = new();

        [Input(Guid = "9890a8ec-9362-4e34-aec4-a6abcc19debd")]
        public readonly InputSlot<bool> Clamping = new InputSlot<bool>();
}