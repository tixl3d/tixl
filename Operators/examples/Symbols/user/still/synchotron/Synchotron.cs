namespace Examples.user.still.synchotron;

[Guid("02625eeb-7f7d-4c48-af5b-13d9a2079b3b")]
internal sealed class Synchotron : Instance<Synchotron>
{
    [Output(Guid = "ad075d63-d346-4fb9-bde6-90824d9a577e")]
    public readonly Slot<Texture2D> Output = new();


}