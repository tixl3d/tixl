namespace Examples.user.still.worksforeverybody.scenes;

[Guid("d71f9aa0-687f-4537-9154-eed685a8ecc2")]
internal sealed class TVIntroScene : Instance<TVIntroScene>
{
    [Output(Guid = "2bdf54ac-ce7b-4f71-802f-0adb54e02083")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Input(Guid = "97ba38a4-78d5-4d27-ae3f-8859bddbfbba")]
    public readonly InputSlot<Texture2D> TvImage = new();


}