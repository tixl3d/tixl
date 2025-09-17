namespace Examples.user.pixtur.examples;

[Guid("b0248e6e-a82b-48d1-ac65-ee7b36038478")]
public class TutorialTitle : Instance<TutorialTitle>
{
    [Output(Guid = "ad10e89c-3350-495d-a992-5f7d371defc8")]
    public readonly Slot<Texture2D> ColorBuffer = new();

    [Input(Guid = "9b430a01-9dfe-4fe5-a946-29496e58f82f")]
    public readonly InputSlot<string> Input = new();

    [Input(Guid = "2af05bc2-8589-4155-8daa-a3e81e4a8766")]
    public readonly InputSlot<string> Category = new();


}