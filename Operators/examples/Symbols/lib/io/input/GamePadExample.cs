namespace Examples.Lib.io.input;

[Guid("025285fd-c00f-44c7-8698-363ccc763fa1")]
 internal sealed class GamePadExample : Instance<GamePadExample>
{
    [Output(Guid = "df202c9a-aad1-4ca8-a12d-a61e48bdb415")]
    public readonly Slot<Texture2D> ColorBuffer = new Slot<Texture2D>();


}