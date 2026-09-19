namespace Examples.howto;

[Guid("e6a11c29-11f9-49a3-9eff-463a93503420")]
internal sealed class HowToUsePoints : Instance<HowToUsePoints>
{

    [Output(Guid = "e4d2b739-0a14-4e52-a275-256b78b12b0f")]
    public readonly Slot<Texture2D> Output2 = new();


}