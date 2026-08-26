namespace Examples.user.still.there.scenes;

[Guid("f56a981c-2080-4617-b9f1-d7a625a44b57")]
internal sealed class WispsParticles : Instance<WispsParticles>
{

    [Output(Guid = "5eaa9c88-6e90-4583-a7bb-457ff99f0a1b")]
    public readonly TimeClipSlot<Command> Output2 = new();


}