namespace Examples.Lib.io.audio;

[Guid("012119bf-aeec-4134-b7aa-6bc7f9816800")]
 internal sealed class SoundInputExample : Instance<SoundInputExample>
{
    [Output(Guid = "e7ce8558-0fd1-4355-8c9e-dac6f0a3b757")]
    public readonly Slot<Texture2D> TextureOutput = new();


}